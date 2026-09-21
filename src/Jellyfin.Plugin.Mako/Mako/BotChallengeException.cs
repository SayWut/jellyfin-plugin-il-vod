namespace Jellyfin.Plugin.Mako.MakoApi;

/// <summary>
/// mako's Radware bot protection intercepted a request: a redirect landed on
/// <see cref="MakoUrls.BotChallengeHost"/>. Callers back off <see cref="MakoUrls.BrowseHost"/>
/// traffic for a while rather than retrying (N4). Per N3 the message never carries a query
/// string, so the URL is reduced to its host and path before it is written into it.
/// </summary>
public sealed class BotChallengeException : Exception
{
    public BotChallengeException(string url)
        : base($"mako: bot challenge while requesting {HostAndPath(url)}")
    {
    }

    private static string HostAndPath(string url)
    {
        Uri uri = new(url);
        return $"{uri.Host}{uri.AbsolutePath}";
    }
}
