using System.Text.Json;
using System.Text.RegularExpressions;
using MediaPager.App.PluginContracts;

namespace MediaPager.Plugins.Stream.VidSrc;

/// <summary>Builds VidSrc-specific requests and delegates all HTTP/browser work to the
/// host-owned broker. The plugin never loads Playwright or receives browser objects.</summary>
internal sealed class PlaywrightResolver(IBrowserAutomationBroker browser)
{
    private static readonly Regex ImdbId = new(@"^tt\d+$", RegexOptions.CultureInvariant);
    private static readonly Regex TmdbId = new(@"^\d+$", RegexOptions.CultureInvariant);
    private static readonly string[] BlockedUrlSubstrings =
    [
        "disable-devtool", "/beacon.min.js", "histats", "dtscout", "sharethis", "tynt",
        "eyeota", "lijit", "onaudience", "crwdcntrl", "adsrvr", "rlcdn", "tapad",
        "33across", "agkn", "ml314", "mrktmtrcs",
    ];

    public async Task<string?> ResolveAsync(
        string host,
        string userAgent,
        StreamResolveRequest request,
        CancellationToken cancellationToken)
    {
        var isImdb = ImdbId.IsMatch(request.ExternalId);
        var isTmdb = TmdbId.IsMatch(request.ExternalId);
        if (!isImdb && !isTmdb) return null;

        var isTv = request.Kind == MediaKind.Tv;
        if (isTv && (request.Season is null || request.Episode is null)) return null;

        var apiParam = isImdb ? $"imdb={request.ExternalId}" : $"tmdb={request.ExternalId}";
        var apiUri = isTv
            ? new Uri($"https://data.{host}/api.php?type=tv&{apiParam}&season={request.Season}&episode={request.Episode}")
            : new Uri($"https://data.{host}/api.php?type=movie&{apiParam}");

        using (var apiJson = JsonDocument.Parse(await browser.GetPublicTextAsync(apiUri, cancellationToken)))
        {
            if (!apiJson.RootElement.TryGetProperty("status_code", out var statusElement))
                return null;
            var statusCode = statusElement.ValueKind == JsonValueKind.Number
                ? statusElement.GetInt32()
                : int.TryParse(statusElement.GetString(), out var parsed) ? parsed : 0;
            if (statusCode != 200) return null;
        }

        var embedPath = (isTv, isImdb) switch
        {
            (true, _) => $"embed/tv/{request.ExternalId}/{request.Season}/{request.Episode}",
            (false, true) => $"embed/{request.ExternalId}",
            _ => $"embed/movie/{request.ExternalId}",
        };
        var embedUri = new Uri($"https://{host}/{embedPath}");
        return await browser.NavigateAndWaitForRequestAsync(new BrowserAutomationRequest(
            embedUri,
            userAgent,
            BlockedUrlSubstrings,
            RequestUrlContains: "/master.m3u8",
            ClickSelector: "#bigPlay",
            TimeoutSeconds: 30), cancellationToken);
    }
}
