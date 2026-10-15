using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.IlVod.Transcoding;

/// <summary>
/// Rewrites the ffmpeg <c>-probesize</c> option in a command line Jellyfin has already built.
/// Pure and Jellyfin-agnostic on purpose: it knows nothing about streams or media sources, only
/// about the text of a command line, which keeps its behaviour easy to pin down with tests.
/// </summary>
public static class ProbeSizeLimiter
{
    /// <summary>
    /// The probe size mako's own streams are limited to: large enough for ffmpeg to identify
    /// H.264 video and AAC audio from the first segments, small enough that mako's
    /// unidentifiable data PID cannot make ffmpeg buffer the whole episode in RAM before giving
    /// up on it (measured: 1G probesize -> ~440 MB RSS on a 200 MB stream; 5M -> 34 MB).
    /// </summary>
    public const string MakoProbeSize = "20M";

    // Matches "-probesize <value>" only where "-probesize" starts a command-line token (at the
    // start of the string or after whitespace), so text that merely contains the word — such as
    // a URL query value — is left alone. Group 1 keeps whatever preceded the flag so replacing
    // does not disturb the surrounding whitespace.
    private static readonly Regex ProbeSizeOption = new(@"(^|\s)-probesize\s+\S+", RegexOptions.Compiled);

    public static string Limit(string commandLineArguments) =>
        ProbeSizeOption.Replace(commandLineArguments, $"$1-probesize {MakoProbeSize}");
}
