namespace Jellyfin.Plugin.IlVod.MakoApi;

/// <summary>
/// mako's Radware bot protection intercepted a request: a redirect landed on
/// <see cref="MakoUrls.BotChallengeHost"/>, or the request was refused outright because
/// <see cref="RequestPacer.IsBackingOff"/> was already true. Callers back off
/// <see cref="MakoUrls.BrowseHost"/> traffic for a while rather than retrying (N4). Per N3 the
/// message never carries a query string — <see cref="MakoUrls.RedactedUrl"/> is the one place
/// that guarantees it.
/// </summary>
public sealed class BotChallengeException : Exception
{
    public BotChallengeException(string url)
        : base($"mako: bot challenge while requesting {MakoUrls.RedactedUrl(url)}")
    {
    }
}
