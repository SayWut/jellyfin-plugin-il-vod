using Jellyfin.Plugin.Mako.Browsing;
using Jellyfin.Plugin.Mako.Model;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.Mako.Tests;

public class MediaSourceFactoryTests
{
    [Fact]
    public void DescribesARemoteHttpStreamThatCanBePlayedDirectly()
    {
        var link = new StreamLink("https://cdn.test/master.m3u8?hdnea=t", "hls");

        MediaSourceInfo source = MediaSourceFactory.Create(link);

        Assert.Equal("https://cdn.test/master.m3u8?hdnea=t", source.Path);
        Assert.Equal(MediaProtocol.Http, source.Protocol);
        Assert.Equal("hls", source.Container);
        Assert.True(source.IsRemote);
        Assert.True(source.SupportsDirectPlay);
        Assert.True(source.SupportsDirectStream);
        Assert.False(source.IsInfiniteStream);
        Assert.False(source.RequiresOpening);
    }

    [Fact]
    public void AnEmptyContainerBecomesNullSoJellyfinDecides()
    {
        var link = new StreamLink("https://cdn.test/master.m3u8?hdnea=t", string.Empty);

        MediaSourceInfo source = MediaSourceFactory.Create(link);

        Assert.Null(source.Container);
    }

    [Fact]
    public void LeavesTheIdEmptySoJellyfinAssignsTheItemsGuid()
    {
        var link = new StreamLink("https://cdn.test/master.m3u8", string.Empty);

        MediaSourceInfo source = MediaSourceFactory.Create(link);

        Assert.Null(source.Id);
    }

    [Fact]
    public void DescribesTheVideoAndAudioSoJellyfinCanCopyThemInsteadOfGuessing()
    {
        MediaSourceInfo source = MediaSourceFactory.Create(new StreamLink("https://cdn.test/master.m3u8", string.Empty));

        MediaStream video = Assert.Single(source.MediaStreams, stream => stream.Type == MediaStreamType.Video);
        MediaStream audio = Assert.Single(source.MediaStreams, stream => stream.Type == MediaStreamType.Audio);
        Assert.Equal("h264", video.Codec);
        Assert.Equal("aac", audio.Codec);
        Assert.NotEqual(video.Index, audio.Index);
    }

    [Fact]
    public void LimitsHowLongFfmpegAnalysesTheStreamBeforePlaying()
    {
        MediaSourceInfo source = MediaSourceFactory.Create(new StreamLink("https://cdn.test/master.m3u8", string.Empty));

        Assert.Equal(3000, source.AnalyzeDurationMs);
    }

    [Fact]
    public void IsMakoSourceIsTrueForASourceThePluginCreated()
    {
        MediaSourceInfo source = MediaSourceFactory.Create(new StreamLink("https://cdn.test/master.m3u8", string.Empty));

        Assert.True(MediaSourceFactory.IsMakoSource(source));
    }

    [Fact]
    public void IsMakoSourceIsFalseForNull()
    {
        Assert.False(MediaSourceFactory.IsMakoSource(null));
    }

    [Fact]
    public void IsMakoSourceIsFalseForASourceWithAnotherName()
    {
        var source = new MediaSourceInfo { Name = "some-other-plugin" };

        Assert.False(MediaSourceFactory.IsMakoSource(source));
    }
}
