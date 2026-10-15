namespace Jellyfin.Plugin.IlVod.Model;

/// <summary>
/// What Jellyfin needs to know about a stream's video to decide whether it can copy it instead
/// of re-encoding it: resolution, bitrate, and the H.264 profile/level (spec §5.2).
/// </summary>
public sealed record StreamDescription(int? Width, int? Height, int? BitsPerSecond, string VideoProfile, double? VideoLevel)
{
    /// <summary>The episode's length, measured from the variant playlist at play time;
    /// <c>null</c> when it could not be measured. Without it Jellyfin treats the stream as live.</summary>
    public TimeSpan? Duration { get; init; }

    private const string UnmeasuredProfile = "Main";

    /// <summary>
    /// What mako's own streams were measured as when no better information is available
    /// (no master playlist variant, or one with no <c>CODECS</c> attribute).
    /// </summary>
    public static StreamDescription MakoDefault { get; } = new(null, null, null, UnmeasuredProfile, null);
}
