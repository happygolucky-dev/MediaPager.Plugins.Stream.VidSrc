using MediaPager.App.PluginContracts;
using MediaPager.Plugins.Stream.VidSrc;

namespace MediaPager.Plugins.Stream.VidSrc;

/// <summary>
/// Community stream provider that prefers a listed 1080p HLS variant when resolving Vidsrc embeds.
/// Implements the MediaPager plugin SDK (IStreamProviderPlugin). This is not an official
/// Nobugsgiven plugin; it is authored and maintained by the community under its own account.
/// </summary>
public sealed class VidsrcProviderPlugin(
    IPluginSettingsStore settingsStore,
    IBrowserAutomationBroker browserAutomation) :
    IPluginSettingsSchema, IStreamProviderPlugin, IPluginActions
{
    public const string PluginKey = "vidsrc";
    public const string HostSetting = "host";
    public const string UserAgentSetting = "userAgent";

    public const string DefaultHost = "vidsrc.sh";
    public const string DefaultUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154.0.0.0 Safari/537.36";

    public const string SourceKeyValue = "vidsrc";
    public const string TvSourceKeyValue = "vidsrc-tv";

    public PluginDescriptor Descriptor { get; } = new(
        Id: "mediapager.stream.vidsrc",
        Name: "Vidsrc",
        Version: "0.1.0",
        Author: "Community",
        Description: "Prefers a 1080p HLS variant when available; otherwise uses the master playlist.");

    public IReadOnlyList<PluginSettingDefinition> Settings { get; } =
    [
        new PluginSettingDefinition(HostSetting, "Embed host", PluginSettingType.String, Default: DefaultHost),
        new PluginSettingDefinition(UserAgentSetting, "Browser user agent", PluginSettingType.String, Default: DefaultUserAgent),
    ];

    private readonly PlaywrightResolver _resolver = new(browserAutomation);

    public IReadOnlyList<SourceDescriptor> Sources { get; } =
    [
        new SourceDescriptor(SourceKeyValue, "Vidsrc", MediaKind.Movie),
        new SourceDescriptor(TvSourceKeyValue, "Vidsrc", MediaKind.Tv),
    ];

    public IReadOnlyList<MediaKind> SupportedKinds { get; } = [MediaKind.Movie, MediaKind.Tv];

    public IReadOnlyList<PluginActionDescriptor> Actions { get; } =
    [
        new("edit-metadata", "Edit metadata", "edit", PluginActionSurface.PosterCard,
            PluginActionPosition.TopRight, 110, [MediaKind.Movie, MediaKind.Tv],
            Click: PluginActionClick.HostAction, Scope: PluginActionScope.LibraryItem,
            HostActionId: PluginHostActionIds.EditCatalogItem),
        new("edit-metadata", "Edit metadata", "edit", PluginActionSurface.DetailScreen,
            PluginActionPosition.TopRight, 100, [MediaKind.Movie, MediaKind.Tv],
            Click: PluginActionClick.HostAction, Scope: PluginActionScope.LibraryItem,
            HostActionId: PluginHostActionIds.EditCatalogItem),
    ];

    public Task<IReadOnlyList<PluginActionDescriptor>> GetActionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Actions);

    public Task InvokeActionAsync(PluginActionContext context, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<Paged<BrowseItem>> BrowseAsync(string sourceKey, string? query, int page, CancellationToken cancellationToken) =>
        Task.FromResult<Paged<BrowseItem>>(new([]));

    public Task<TitleDetails?> GetDetailsAsync(string sourceKey, string externalId, CancellationToken cancellationToken) =>
        Task.FromResult<TitleDetails?>(null);

    public async Task<StreamResult?> ResolveAsync(StreamResolveRequest request, CancellationToken cancellationToken)
    {
        var sourceKind = request.SourceKey switch
        {
            SourceKeyValue => MediaKind.Movie,
            TvSourceKeyValue => MediaKind.Tv,
            _ => (MediaKind?)null,
        };
        if (sourceKind is null || sourceKind != request.Kind)
            return null;

        var host = (await settingsStore.GetAsync(PluginKey, HostSetting, cancellationToken))?.Trim();
        if (string.IsNullOrWhiteSpace(host))
            host = DefaultHost;

        var userAgent = (await settingsStore.GetAsync(PluginKey, UserAgentSetting, cancellationToken))?.Trim();
        if (string.IsNullOrWhiteSpace(userAgent))
            userAgent = DefaultUserAgent;

        var url = await _resolver.ResolveAsync(host, userAgent, request, cancellationToken);
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var upstream))
            return null;

        return new StreamResult(upstream);
    }
}
