using System.Globalization;

namespace Jellyfin.Plugin.IlVod.Hls;

/// <summary>
/// An HLS media (variant) playlist — the one that lists segments: where an episode's length can be
/// measured when a provider states it nowhere in its own data (mako does not).
/// </summary>
public static class MediaPlaylist
{
    private const string SegmentDurationTag = "#EXTINF:";
    private const char DurationTitleSeparator = ',';

    /// <summary>The sum of every segment's <c>#EXTINF</c> duration; <c>null</c> when the text
    /// holds no measurable segment at all (so "no duration" is never mistaken for "zero").</summary>
    public static TimeSpan? TotalDuration(string mediaPlaylistText)
    {
        List<double> segmentSeconds = mediaPlaylistText
            .Split('\n')
            .Select(line => TryReadSegmentSeconds(line.Trim()))
            .OfType<double>()
            .ToList();

        return segmentSeconds.Count == 0 ? null : TimeSpan.FromSeconds(segmentSeconds.Sum());
    }

    private static double? TryReadSegmentSeconds(string line)
    {
        if (!line.StartsWith(SegmentDurationTag, StringComparison.Ordinal))
        {
            return null;
        }

        string duration = line[SegmentDurationTag.Length..].Split(DurationTitleSeparator)[0];
        return double.TryParse(duration, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && seconds >= 0
            ? seconds
            : null;
    }
}
