using System.Globalization;
using System.Text.RegularExpressions;

namespace MediaPager.Plugins.Stream.VidSrc;

internal static class HlsMasterPlaylist
{
    private static readonly Regex ResolutionAttribute = new(
        @"(?:^|,)\s*RESOLUTION\s*=\s*\d+\s*[xX]\s*(?<height>\d+)\s*(?:,|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BandwidthAttribute = new(
        @"(?:^|,)\s*BANDWIDTH\s*=\s*(?<bandwidth>\d+)\s*(?:,|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AudioGroupAttribute = new(
        @"(?:^|,)\s*AUDIO\s*=\s*""[^""]+""(?:\s*(?:,|$))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static Uri? Find1080Variant(Uri masterPlaylistUri, string playlist)
    {
        ArgumentNullException.ThrowIfNull(masterPlaylistUri);
        ArgumentNullException.ThrowIfNull(playlist);

        var lines = playlist.Split('\n');
        Uri? selectedUri = null;
        long selectedBandwidth = -1;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            const string streamInfoPrefix = "#EXT-X-STREAM-INF:";
            if (!line.StartsWith(streamInfoPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var attributes = line[streamInfoPrefix.Length..];
            // Returning one media playlist cannot preserve an external audio rendition.
            if (AudioGroupAttribute.IsMatch(attributes))
                continue;

            var resolutionMatch = ResolutionAttribute.Match(attributes);
            if (!resolutionMatch.Success ||
                !int.TryParse(resolutionMatch.Groups["height"].Value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var height) || height != 1080)
                continue;

            var uriLine = FindVariantUri(lines, index + 1);
            if (uriLine is null || !Uri.TryCreate(masterPlaylistUri, uriLine, out var variantUri) ||
                variantUri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(variantUri.UserInfo))
                continue;

            var bandwidthMatch = BandwidthAttribute.Match(attributes);
            var bandwidth = bandwidthMatch.Success &&
                long.TryParse(bandwidthMatch.Groups["bandwidth"].Value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var parsedBandwidth)
                ? parsedBandwidth
                : 0;

            if (bandwidth <= selectedBandwidth)
                continue;

            selectedUri = variantUri;
            selectedBandwidth = bandwidth;
        }

        return selectedUri;
    }

    private static string? FindVariantUri(IReadOnlyList<string> lines, int startIndex)
    {
        for (var index = startIndex; index < lines.Count; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0)
                continue;
            if (line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.OrdinalIgnoreCase))
                return null;
            if (line[0] == '#')
                continue;
            return line;
        }

        return null;
    }
}
