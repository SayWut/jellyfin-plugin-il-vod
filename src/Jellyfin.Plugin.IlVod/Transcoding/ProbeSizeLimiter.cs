using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.IlVod.Transcoding;

/// <summary>
/// Rewrites the ffmpeg <c>-probesize</c> option in a command line Jellyfin has already built.
/// Pure and Jellyfin-agnostic on purpose: it knows nothing about streams or media sources, only
/// about the text of a command line, which keeps its behaviour easy to pin down with tests.
/// </summary>
public static class ProbeSizeLimiter
{
    // Matches "-probesize <value>" only where "-probesize" starts a command-line token (at the
    // start of the string or after whitespace), so text that merely contains the word — such as
    // a URL query value — is left alone. Group 1 keeps whatever preceded the flag so replacing
    // does not disturb the surrounding whitespace.
    private static readonly Regex ProbeSizeOption = new(@"(^|\s)-probesize\s+\S+", RegexOptions.Compiled);

    /// <param name="probeSize">An ffmpeg size such as <c>20M</c>; each provider names its own
    /// (<see cref="Core.ProviderInfo.ProbeSize"/>).</param>
    public static string Limit(string commandLineArguments, string probeSize) =>
        ProbeSizeOption.Replace(commandLineArguments, $"$1-probesize {probeSize}");
}
