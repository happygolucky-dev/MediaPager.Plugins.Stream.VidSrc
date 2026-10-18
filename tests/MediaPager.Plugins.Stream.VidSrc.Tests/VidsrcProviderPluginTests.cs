using System.Net.Http;
using MediaPager.App.PluginContracts;
using MediaPager.Plugins.Stream.VidSrc;
using Xunit;

namespace MediaPager.Plugins.Stream.VidSrc.Tests;

public sealed class VidsrcProviderPluginTests
{
    private const string MasterUrl = "https://media.example.test/video/master.m3u8?master=keep";

    [Fact]
    public async Task Resolve_selects_highest_bandwidth_1080p_variant()
    {
        const string playlist = """
            #EXTM3U
            #EXT-X-STREAM-INF:BANDWIDTH=1800000,RESOLUTION=1280x720
            720/index.m3u8
            #EXT-X-STREAM-INF:BANDWIDTH=9000000,RESOLUTION=2560x1440
            1440/index.m3u8
            #EXT-X-STREAM-INF:BANDWIDTH=4500000,RESOLUTION=1920x1080
            ../1080/low.m3u8?token=low
            #EXT-X-STREAM-INF:BANDWIDTH=6000000,RESOLUTION=1920x1080
            ../1080/high.m3u8?token=high
            """;
        var browser = new FakeBrowserAutomationBroker(MasterUrl, playlist);

        var result = await CreatePlugin(browser).ResolveAsync(MovieRequest, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("https://media.example.test/1080/high.m3u8?token=high", result.UpstreamUri.AbsoluteUri);
        Assert.Null(result.ContentType);
        Assert.False(result.DirectPlayback);
    }

    [Theory]
    [InlineData("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1800000,RESOLUTION=1280x720\n720/index.m3u8")]
    [InlineData("not an HLS playlist")]
    [InlineData("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=6000000,RESOLUTION=1920x1080\n#EXT-X-STREAM-INF:BANDWIDTH=4000000,RESOLUTION=1920x1080\n")]
    [InlineData("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=6000000,RESOLUTION=1920x1080,AUDIO=\"main\"\n1080/index.m3u8")]
    public async Task Resolve_returns_master_when_no_usable_1080p_variant_is_listed(string playlist)
    {
        var browser = new FakeBrowserAutomationBroker(MasterUrl, playlist);

        var result = await CreatePlugin(browser).ResolveAsync(MovieRequest, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(MasterUrl, result.UpstreamUri.AbsoluteUri);
    }

    [Fact]
    public async Task Resolve_returns_master_when_playlist_fetch_fails()
    {
        var browser = new FakeBrowserAutomationBroker(MasterUrl, string.Empty)
        {
            FailMasterPlaylistFetch = true,
        };

        var result = await CreatePlugin(browser).ResolveAsync(MovieRequest, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(MasterUrl, result.UpstreamUri.AbsoluteUri);
    }

    [Fact]
    public async Task Resolve_propagates_caller_cancellation_during_playlist_fetch()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var browser = new FakeBrowserAutomationBroker(MasterUrl, string.Empty)
        {
            CancelMasterPlaylistFetch = true,
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreatePlugin(browser).ResolveAsync(MovieRequest, cancellation.Token));
    }

    private static VidsrcProviderPlugin CreatePlugin(IBrowserAutomationBroker browser) =>
        new(new FakePluginSettingsStore(), browser);

    private static StreamResolveRequest MovieRequest =>
        new(VidsrcProviderPlugin.SourceKeyValue, "12345", MediaKind.Movie);

    private sealed class FakePluginSettingsStore : IPluginSettingsStore
    {
        public Task<string?> GetAsync(string pluginKey, string name, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task SetAsync(string pluginKey, string name, string? value, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(
            string pluginKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
    }

    private sealed class FakeBrowserAutomationBroker(
        string masterUrl,
        string masterPlaylist) : IBrowserAutomationBroker
    {
        public bool FailMasterPlaylistFetch { get; init; }
        public bool CancelMasterPlaylistFetch { get; init; }

        public Task<string> GetPublicTextAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            if (uri.AbsolutePath.EndsWith("/api.php", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult("{\"status_code\":200}");

            if (FailMasterPlaylistFetch)
                throw new HttpRequestException("Master playlist fetch failed.");
            if (CancelMasterPlaylistFetch)
                throw new OperationCanceledException(cancellationToken);

            return Task.FromResult(masterPlaylist);
        }

        public Task<string?> NavigateAndWaitForRequestAsync(
            BrowserAutomationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(masterUrl);
    }
}
