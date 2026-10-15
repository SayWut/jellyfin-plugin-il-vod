using System.Net;
using Jellyfin.Plugin.IlVod.Providers.Mako;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Mako;

/// <summary>
/// The exact handler the production <c>HttpClient</c> is built over (task B10's DI wiring).
/// <see cref="MakoHttpClient"/>'s own redirect handling depends on <c>AllowAutoRedirect = false</c>
/// — it follows redirects by hand so a bot-challenge redirect can be told apart from an ordinary
/// one (see <c>MakoHttpClient.ResolveRedirectOrThrow</c>).
/// </summary>
public class MakoHttpHandlerFactoryTests
{
    [Fact]
    public void NeverFollowsRedirectsAutomatically()
    {
        using SocketsHttpHandler handler = MakoHttpHandlerFactory.CreateHandler();

        Assert.False(handler.AllowAutoRedirect);
    }

    [Fact]
    public void DecompressesEveryEncodingMakoMightSend()
    {
        using SocketsHttpHandler handler = MakoHttpHandlerFactory.CreateHandler();

        Assert.Equal(DecompressionMethods.All, handler.AutomaticDecompression);
    }
}
