namespace Jellyfin.Plugin.IlVod.Core;

/// <summary>
/// What Jellyfin needs to know about a stream's video to decide whether it can copy it instead
/// of re-encoding it: resolution, bitrate, and the H.264 profile/level (spec §5.2).
/// </summary>
public sealed record StreamDescription(int? Width, int? Height, int? BitsPerSecond, string VideoProfile, double? VideoLevel)
{
    /// <summary>The episode's length, measured from the variant playlist at play time;
    /// <c>null</c> when it could not be measured. Without it Jellyfin treats the stream as live.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>The stream's codecs; <c>null</c> when the provider cannot say, in which case
    /// Jellyfin is told nothing about the streams and probes them itself.</summary>
    public StreamFormat? Format { get; init; }

    /// <summary>Which of the stream's tracks ffmpeg should map as the video, by its input index.
    /// 0 for a stream whose video comes first — every muxed one; a master playlist with a separate
    /// audio rendition numbers its tracks otherwise (<c>Hls/MasterPlaylist.FfmpegStreamIndices</c>).</summary>
    public int VideoStreamIndex { get; init; }

    /// <summary>Which of the stream's tracks ffmpeg should map as the audio, by its input index.</summary>
    public int AudioStreamIndex { get; init; } = 1;
}
