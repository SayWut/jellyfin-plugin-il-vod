using Jellyfin.Plugin.IlVod.Browsing;
using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Providers.Mako;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.IlVod.Tests;

public class MediaSourceFactoryTests
{
    [Fact]
    public void DescribesARemoteHttpStreamThatCanBePlayedDirectly()
    {
        var link = new StreamLink("https://cdn.test/master.m3u8?hdnea=t", "hls", MakoStreams.DefaultDescription);

        MediaSourceInfo source = MediaSourceFactory.Create(link, "mako");

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
        StreamDescription measured = MakoStreams.DefaultDescription with { Duration = TimeSpan.FromSeconds(1320.08) };
        var link = new StreamLink("https://cdn.test/master.m3u8", "hls", measured);

        Assert.Equal(TimeSpan.FromSeconds(1320.08).Ticks, MediaSourceFactory.Create(link, "mako").RunTimeTicks);
    }

    [Fact]
    public void AnUnmeasuredDurationLeavesTheRunTimeUnset()
    {
        var link = new StreamLink("https://cdn.test/master.m3u8", "hls", MakoStreams.DefaultDescription);

        Assert.Null(MediaSourceFactory.Create(link, "mako").RunTimeTicks);
    }

    [Fact]
    public void AnEmptyContainerBecomesNullSoJellyfinDecides()
    {
        var link = new StreamLink("https://cdn.test/master.m3u8?hdnea=t", string.Empty, MakoStreams.DefaultDescription);

        MediaSourceInfo source = MediaSourceFactory.Create(link, "mako");

        Assert.Null(source.Container);
    }

    [Fact]
    public void LeavesTheIdEmptySoJellyfinAssignsTheItemsGuid()
    {
        var link = new StreamLink("https://cdn.test/master.m3u8", string.Empty, MakoStreams.DefaultDescription);

        MediaSourceInfo source = MediaSourceFactory.Create(link, "mako");

        Assert.Null(source.Id);
    }

    [Fact]
    public void DescribesTheVideoAndAudioSoJellyfinCanCopyThemInsteadOfGuessing()
    {
        MediaSourceInfo source = MediaSourceFactory.Create(
            new StreamLink("https://cdn.test/master.m3u8", string.Empty, MakoStreams.DefaultDescription),
            "mako");

        MediaStream video = Assert.Single(source.MediaStreams, stream => stream.Type == MediaStreamType.Video);
        MediaStream audio = Assert.Single(source.MediaStreams, stream => stream.Type == MediaStreamType.Audio);
        Assert.Equal("h264", video.Codec);
        Assert.Equal("aac", audio.Codec);
        Assert.NotEqual(video.Index, audio.Index);
    }

    [Fact]
    public void ByDefaultTheVideoIsTheFirstStreamAndTheAudioTheSecond()
    {
        MediaSourceInfo source = MediaSourceFactory.Create(
            new StreamLink("https://cdn.test/master.m3u8", string.Empty, MakoStreams.DefaultDescription),
            "mako");

        Assert.Equal(0, Assert.Single(source.MediaStreams, stream => stream.Type == MediaStreamType.Video).Index);
        Assert.Equal(1, Assert.Single(source.MediaStreams, stream => stream.Type == MediaStreamType.Audio).Index);
    }

    /// <summary>
    /// Jellyfin numbers a stream for ffmpeg's <c>-map 0:N</c> by its position in the source's list
    /// (<c>EncodingHelper.FindIndex</c>), never by its <c>Index</c> — measured on the phone, where a
    /// two-stream list mapped Kan's 540p variant. So a master whose audio is track 0 and whose best
    /// video is track 5 lists six streams, with ones Jellyfin never picks in between.
    /// </summary>
    [Fact]
    public void EachStreamSitsWhereFfmpegNumbersIt()
    {
        StreamDescription description = MakoStreams.DefaultDescription with { VideoStreamIndex = 5, AudioStreamIndex = 0 };
        MediaSourceInfo source = MediaSourceFactory.Create(
            new StreamLink("https://cdn.test/master.m3u8", string.Empty, description),
            "kan");

        MediaStream video = Assert.Single(source.MediaStreams, stream => stream.Type == MediaStreamType.Video);
        MediaStream audio = Assert.Single(source.MediaStreams, stream => stream.Type == MediaStreamType.Audio);
        Assert.Equal(5, EncodingHelper.FindIndex(source.MediaStreams, video));
        Assert.Equal(0, EncodingHelper.FindIndex(source.MediaStreams, audio));
        Assert.Equal((5, 0), (video.Index, audio.Index));
        Assert.All(source.MediaStreams.Except([video, audio]), filler => Assert.Equal(MediaStreamType.Data, filler.Type));
    }

    [Fact]
    public void ByDefaultTheVideoIsMappedFirstAndTheAudioSecond()
    {
        MediaSourceInfo source = MediaSourceFactory.Create(
            new StreamLink("https://cdn.test/master.m3u8", string.Empty, MakoStreams.DefaultDescription),
            "mako");

        Assert.Equal([MediaStreamType.Video, MediaStreamType.Audio], source.MediaStreams.Select(stream => stream.Type));
    }

    [Fact]
    public void TheVideoStreamCarriesTheStreamDescription()
    {
        var description = new StreamDescription(1920, 1080, 5_000_000, "High", 4.0) { Format = MakoStreams.Format };
        MediaSourceInfo source = MediaSourceFactory.Create(
            new StreamLink("https://cdn.test/master.m3u8", string.Empty, description),
            "mako");

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
            new StreamLink("https://cdn.test/master.m3u8", string.Empty, MakoStreams.DefaultDescription),
            "mako");

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
            new StreamLink("https://cdn.test/master.m3u8", string.Empty, MakoStreams.DefaultDescription),
            "mako");

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
            new StreamLink("https://cdn.test/master.m3u8", string.Empty, MakoStreams.DefaultDescription),
            "mako");

        MediaStream audio = Assert.Single(source.MediaStreams, stream => stream.Type == MediaStreamType.Audio);
        Assert.Equal(48000, audio.SampleRate);
        Assert.Equal("LC", audio.Profile);
    }

    [Fact]
    public void ADescriptionWithNoFormatDescribesNoStreams()
    {
        MediaSourceInfo source = MediaSourceFactory.Create(
            new StreamLink("https://cdn.test/master.m3u8", string.Empty, new StreamDescription(null, null, null, "Main", null)),
            "mako");

        Assert.Empty(source.MediaStreams);
    }

    [Fact]
    public void LimitsHowLongFfmpegAnalysesTheStreamBeforePlaying()
    {
        MediaSourceInfo source = MediaSourceFactory.Create(
            new StreamLink("https://cdn.test/master.m3u8", string.Empty, MakoStreams.DefaultDescription),
            "mako");

        Assert.Equal(3000, source.AnalyzeDurationMs);
    }

    [Fact]
    public void ASourceThePluginCreatedNamesItsProvider()
    {
        MediaSourceInfo source = MediaSourceFactory.Create(
            new StreamLink("https://cdn.test/master.m3u8", string.Empty, MakoStreams.DefaultDescription),
            "mako");

        Assert.Equal("il-vod/mako", source.Name);
        Assert.Equal("mako", MediaSourceFactory.ProviderIdOf(source));
    }

    [Fact]
    public void NullIsNoProvidersSource()
    {
        Assert.Null(MediaSourceFactory.ProviderIdOf(null));
    }

    [Fact]
    public void ASourceWithAnotherNameIsNoProvidersSource()
    {
        var source = new MediaSourceInfo { Name = "some-other-plugin", Protocol = MediaProtocol.Http, IsRemote = true };

        Assert.Null(MediaSourceFactory.ProviderIdOf(source));
    }

    [Fact]
    public void ALocalFileHappeningToBeNamedLikeOurSourcesIsNoProvidersSource()
    {
        // Jellyfin names a library file's media source after its file name, so a local file
        // could otherwise collide with the name this plugin's own streams carry.
        var source = new MediaSourceInfo { Name = "il-vod/mako", Protocol = MediaProtocol.File, IsRemote = false };

        Assert.Null(MediaSourceFactory.ProviderIdOf(source));
    }
}
