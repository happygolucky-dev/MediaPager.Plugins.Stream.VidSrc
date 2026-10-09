using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using MediaPager.App.PluginContracts;

namespace MediaPager.Plugins.Stream.VidSrc;

/// <summary>
/// Owns the shared headless browser and one resolve operation at a time (an embed page can
/// only produce one link at once). Faithful port of the resolve flow that used to live in the
/// app host. Returns null instead of throwing so a failed lookup just means "no stream".
/// </summary>
internal sealed class PlaywrightResolver : IAsyncDisposable
{
    private static readonly Regex ImdbId = new(@"^tt\d+$", RegexOptions.CultureInvariant);
    private static readonly Regex TmdbId = new(@"^\d+$", RegexOptions.CultureInvariant);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    /// <summary>Resolve an id to an HLS master playlist URL, or null when unavailable.</summary>
    public async Task<string?> ResolveAsync(string host, string userAgent, StreamResolveRequest request, CancellationToken cancellationToken)
    {
        var isImdb = ImdbId.IsMatch(request.ExternalId);
        var isTmdb = TmdbId.IsMatch(request.ExternalId);
        if (!isImdb && !isTmdb)
            return null;

        var isTv = request.Kind == MediaKind.Tv;
        if (isTv && (request.Season is null || request.Episode is null))
            return null;

        var apiParam = isImdb ? $"imdb={request.ExternalId}" : $"tmdb={request.ExternalId}";
        var embedPath = (isTv, isImdb) switch
        {
            (true, _) => $"embed/tv/{request.ExternalId}/{request.Season}/{request.Episode}",
            (false, true) => $"embed/{request.ExternalId}",
            _ => $"embed/movie/{request.ExternalId}",
        };

        // Bound the gate wait so a previously stuck lookup can't wedge every later request.
        using var gateTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var gateLinked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, gateTimeout.Token);
        try
        {
            await _gate.WaitAsync(gateLinked.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        IPage? page = null;
        try
        {
            // Step 1: ask the provider API whether a video exists for this id.
            using (var client = new HttpClient())
            {
                var apiUrl = isTv
                    ? $"https://data.{host}/api.php?type=tv&{apiParam}&season={request.Season}&episode={request.Episode}"
                    : $"https://data.{host}/api.php?type=movie&{apiParam}";
                var apiJson = await client.GetStringAsync(apiUrl, cancellationToken);
                var statusElement = JsonDocument.Parse(apiJson).RootElement.GetProperty("status_code");
                var statusCode = statusElement.ValueKind == JsonValueKind.Number
                    ? statusElement.GetInt32()
                    : int.Parse(statusElement.GetString()!);
                if (statusCode != 200)
                    return null;
            }

            // Step 2: load the embed page and capture the HLS master playlist request.
            var context = await (await GetBrowserAsync()).NewContextAsync(new() { UserAgent = userAgent });
            page = await context.NewPageAsync();

            // Block the anti-automation script that wipes the page, plus tracker junk.
            await page.RouteAsync("**/*", async route =>
            {
                if (IsTrackerOrAutomationBlocker(route.Request.Url))
                    await route.AbortAsync();
                else
                    await route.ContinueAsync();
            });

            await page.AddInitScriptAsync("Object.defineProperty(navigator, 'webdriver', { get: () => undefined });");

            var linkFound = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            page.Request += (_, req) =>
            {
                if (req.Url.Contains("/master.m3u8", StringComparison.OrdinalIgnoreCase))
                    linkFound.TrySetResult(req.Url);
            };

            await page.GotoAsync($"https://{host}/{embedPath}");

            // Keep nudging the player (#bigPlay) in every frame until the stream request shows
            // up — the player iframe loads after the main page.
            using var clickLoop = new PeriodicTimer(TimeSpan.FromSeconds(1));
            _ = Task.Run(async () =>
            {
                while (!linkFound.Task.IsCompleted && await clickLoop.WaitForNextTickAsync())
                {
                    foreach (var frame in page.Frames)
                    {
                        try
                        {
                            await frame.EvaluateAsync(
                                "document.querySelector('#bigPlay') && document.querySelector('#bigPlay').click()");
                        }
                        catch
                        {
                            // cross-origin frame
                        }
                    }
                }
            });

            var done = await Task.WhenAny(linkFound.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            return done == linkFound.Task ? linkFound.Task.Result : null;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[{nameof(PlaywrightResolver)}] resolve failed: {exception.Message}");
            return null;
        }
        finally
        {
            if (page is not null)
                await page.Context.CloseAsync();
            _gate.Release();
        }
    }

    private static bool IsTrackerOrAutomationBlocker(string url) =>
        new[]
        {
            "disable-devtool", "/beacon.min.js", "histats", "dtscout", "sharethis", "tynt",
            "eyeota", "lijit", "onaudience", "crwdcntrl", "adsrvr", "rlcdn", "tapad",
            "33across", "agkn", "ml314", "mrktmtrcs",
        }.Any(token => url.Contains(token, StringComparison.OrdinalIgnoreCase));

    private async Task<IBrowser> GetBrowserAsync()
    {
        if (_browser is { IsConnected: true })
            return _browser;

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            Args = ["--disable-blink-features=AutomationControlled"],
        });
        return _browser;
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
            await _browser.CloseAsync();
        _playwright?.Dispose();
        _gate.Dispose();
    }
}