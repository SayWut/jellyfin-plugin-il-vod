using MediaBrowser.Controller.Channels;

namespace Jellyfin.Plugin.Mako.Tests;

public class ToolchainTests
{
    [Fact]
    public void JellyfinChannelInterfaceIsAvailable()
    {
        Assert.True(typeof(IChannel).IsInterface);
    }
}
