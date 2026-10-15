using System.Net;

namespace Jellyfin.Plugin.IlVod.MakoApi;

/// <summary>
/// Builds the <see cref="SocketsHttpHandler"/> the production <c>HttpClient</c> is built over
/// (task B10's DI wiring). Pulled out of the DI registration itself so a test can call exactly the
/// function production wiring calls, rather than inspecting a constructed <c>HttpClient</c> that
/// has no public seam for its handler's settings.
/// </summary>
internal static class MakoHttpHandlerFactory
{
    /// <summary>
    /// <see cref="SocketsHttpHandler.AllowAutoRedirect"/> must be <see langword="false"/>:
    /// <see cref="MakoHttpClient"/> follows redirects itself so a redirect to
    /// <see cref="MakoUrls.BotChallengeHost"/> can be told apart from an ordinary one, which the
    /// framework's own automatic redirect handling would hide.
    /// </summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
    };
}
