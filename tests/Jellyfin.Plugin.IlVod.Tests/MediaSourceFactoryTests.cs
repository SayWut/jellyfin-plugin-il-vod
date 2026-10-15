using Jellyfin.Plugin.IlVod.Browsing;
using Jellyfin.Plugin.IlVod.Model;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.IlVod.Tests;

public class MediaSourceFactoryTests
{
    [Fact]
    public void DescribesARemoteHttpStreamThatCanBePlayedDirectly()
    {
        var link = new StreamLink("https://cdn.test/master.m3u8?hdnea=t", "hls", StreamDescription.MakoDefault);

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

    /// <summary>
    /// Jellyfin 12.1 treats a source with no run time as a segmented LIVE stream
    /// (<c>EncodingJobInfo.IsSegmentedLiveStream</c>): it serves a growing EVENT playlist, which
    /// players join near its end — so an episode started 30-60 s in and could not be rewound.
    /// </summary>
    [Fact]
    public void TheMeasuredDurationBecomesTheRunTimeSoJellyfinDoesNotTreatTheStreamAsLive()
    {
        StreamDescription measured = StreamDescription.MakoDefault with { Duration = TimeSpan.FromSeconds(1320.08) };
        var link = new StreamLink("https://cdn.test/master.m3u8", "hls", measured);

        Assert.Equal(TimeSpan.FromSeconds(1320.08).Ticks, MediaSourceFactory.Create(link).RunTimeTicks);
    }

    [Fact]
    public void AnUnmeasuredDurationLeavesTheRunTimeUnset()
    {
        var link = new StreamLink("https://cdn.test/master.m3u8", "hls", StreamDescription.MakoDefault);

        Assert.Null(MediaSourceFactory.Create(link).RunTimeTicks);
    }

    [Fact]
    public void AnEmptyContainerBecomesNullSoJellyfinDecides()
    {
        var link = new StreamLink("https://cdn.test/master.m3u8?hdnea=t", string.Empty, StreamDescription.MakoDefault);

        MediaSourceInfo source = MediaSourceFactory.Create(link);

        Assert.Null(source.Container);
    }

    [Fact]
    public void LeavesTheIdEmptySoJellyfinAssignsTheItemsGuid()
    {
        var link = new StreamLink("https://cdn.test/master.m3u8", string.Empty, StreamDescription.MakoDefault);

        MediaSourceInfo source = MediaSourceFactory.Create(link);

        Assert.Null(source.Id);
    }

    [Fact]
    public void DescribesTheVideoAndAudioSoJellyfinCanCopyThemInsteadOfGuessing()
    {
        MediaSourceInfo source = MediaSourceFactory.Create(
            new StreamLink("https://cdn.test/master.m3u8", string.Empty, StreamDescription.MakoDefault));

        MediaStream video = Assert.Single(source.MediaStreams, stream => stream.Type == MediaStreamType.Video);
        MediaStream audio = Assert.Single(source.MediaStreams, stream => stream.Type == MediaStreamType.Audio);
        Assert.Equal("h264", video.Codec);
        Assert.Equal("aac", audio.Codec);
        Assert.NotEqual(video.Index, audio.Index);
    }

    [Fact]
    public void TheVideoStreamCarriesTheStreamDescription()
    {
        var description = new StreamDescription(1920, 1080, 5_000_000, "High", 4.0);
        MediaSourceInfo source = MediaSourceFactory.Create(
            new StreamLink("https://cdn.test/master.m3u8", string.Empty, description));

        MediaStream video = Assert.Single(source.MediaStreams, stream => stream.Type == MediaStreamType.Video);
        Assert.Equal(1920, video.Width);
        Assert.Equal(1080, video.Height);
        Assert.Equal(5_000_000, video.BitRate);
        Assert.Equal("High", video.Profile);
        Assert.Equal(4.0, video.Level);
    }

    [Fact]
    public void ANullDescriptionLeavesTheVideoStreamsResolutionAndBitrateUnset()
    {
        MediaSourceInfo source = MediaSourceFactory.Create(
            new StreamLink("https://cdn.test/master.m3u8", string.Empty, StreamDescription.MakoDefault));

        MediaStream video = Assert.Single(source.MediaStreams, stream => stream.Type == MediaStreamType.Video);
        Assert.Null(video.Width);
        Assert.Null(video.Height);
        Assert.Null(video.BitRate);
        Assert.Null(video.Level);
    }

    [Fact]
    public void TheVideoStreamCarriesTheFlagsThatLetJellyfinCopyH264()
    {
        MediaSourceInfo source = MediaSourceFactory.Create(
            new StreamLink("https://cdn.test/master.m3u8", string.Empty, StreamDescription.MakoDefault));

        MediaStream video = Assert.Single(source.MediaStreams, stream => stream.Type == MediaStreamType.Video);
        Assert.False(video.IsInterlaced);
        Assert.Equal("yuv420p", video.PixelFormat);
        Assert.Equal(8, video.BitDepth);
        Assert.True(video.IsAVC);
        Assert.Equal("0", video.NalLengthSize);
    }

    [Fact]
    public void TheAudioStreamCarriesTheSampleRateAndProfile()
    {
        MediaSourceInfo source = MediaSourceFactory.Create(
            new StreamLink("https://cdn.test/master.m3u8", string.Empty, StreamDescription.MakoDefault));

        MediaStream audio = Assert.Single(source.MediaStreams, stream => stream.Type == MediaStreamType.Audio);
        Assert.Equal(48000, audio.SampleRate);
        Assert.Equal("LC", audio.Profile);
    }

    [Fact]
    public void LimitsHowLongFfmpegAnalysesTheStreamBeforePlaying()
    {
        MediaSourceInfo source = MediaSourceFactory.Create(
            new StreamLink("https://cdn.test/master.m3u8", string.Empty, StreamDescription.MakoDefault));

        Assert.Equal(3000, source.AnalyzeDurationMs);
    }

    [Fact]
    public void IsMakoSourceIsTrueForASourceThePluginCreated()
    {
        MediaSourceInfo source = MediaSourceFactory.Create(
            new StreamLink("https://cdn.test/master.m3u8", string.Empty, StreamDescription.MakoDefault));

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

    [Fact]
    public void IsMakoSourceIsFalseForALocalFileHappeningToBeNamedMako()
    {
        // Jellyfin names a library file's media source after its file name, so a local
        // mako.mkv would otherwise collide with the name this plugin's own streams carry.
        var source = new MediaSourceInfo { Name = "mako", Protocol = MediaProtocol.File, IsRemote = false };

        Assert.False(MediaSourceFactory.IsMakoSource(source));
    }
}
