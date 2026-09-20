using Jellyfin.Plugin.Mako.Browsing;
using Jellyfin.Plugin.Mako.Model;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.Mako.Tests;

public class MediaSourceFactoryTests
{
    [Fact]
    public void DescribesARemoteHttpStreamThatCanBePlayedDirectly()
    {
        var link = new StreamLink("https://cdn.test/master.m3u8?hdnea=t", "hls");

        MediaSourceInfo source = MediaSourceFactory.Create("shemesh-s1-e1", link);

        Assert.Equal("shemesh-s1-e1", source.Id);
        Assert.Equal("https://cdn.test/master.m3u8?hdnea=t", source.Path);
        Assert.Equal(MediaProtocol.Http, source.Protocol);
        Assert.Equal("hls", source.Container);
        Assert.True(source.IsRemote);
        Assert.True(source.SupportsDirectPlay);
        Assert.True(source.SupportsDirectStream);
        Assert.False(source.IsInfiniteStream);
        Assert.False(source.RequiresOpening);
    }
}
