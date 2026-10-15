namespace Jellyfin.Plugin.IlVod.Transcoding;

/// <summary>
/// Whether <see cref="TranscodeManagerDecoration.Apply"/> managed to install the probe-size
/// limiter into Jellyfin's container. The one place other code checks before assuming ffmpeg's
/// probe size will actually be limited for mako's streams — set once, at server startup, by
/// <see cref="TranscodeManagerDecoration.Apply"/>, and read on every play by
/// <see cref="Jellyfin.Plugin.IlVod.IlVodChannel"/> so a seam that moved under a Jellyfin update
/// shows up in the log instead of failing silently.
/// </summary>
public static class ProbeSizeLimiterStatus
{
    public static bool IsInstalled { get; set; }
}
