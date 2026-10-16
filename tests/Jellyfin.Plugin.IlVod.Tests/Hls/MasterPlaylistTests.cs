using Jellyfin.Plugin.IlVod.Hls;
using Jellyfin.Plugin.IlVod.Tests.Support;

namespace Jellyfin.Plugin.IlVod.Tests.Hls;

public class MasterPlaylistTests
{
    private static readonly string FixturePlaylist = FixtureFile.ReadText("master.m3u8");

    [Fact]
    public void PicksTheHighestBandwidthVariant()
    {
        HlsVariant description = MasterPlaylist.Describe(FixturePlaylist)!;

        Assert.Equal(576, description.Width);
        Assert.Equal(432, description.Height);
        Assert.Equal(1008884, description.BitsPerSecond);
    }

    [Fact]
    public void FallsBackToMainWhenThereAreNoCodecs()
    {
        HlsVariant description = MasterPlaylist.Describe(FixturePlaylist)!;

        Assert.Equal("Main", description.VideoProfile);
        Assert.Null(description.VideoLevel);
    }

    [Fact]
    public void ReadsProfileAndLevelFromCodecs()
    {
        // The comma inside the quoted CODECS value, followed by another attribute, exercises
        // attribute parsing that must not split on a comma inside quotes.
        const string playlist = """
            #EXTM3U
            #EXT-X-STREAM-INF:BANDWIDTH=2500000,CODECS="avc1.64001F,mp4a.40.2",RESOLUTION=1920x1080
            variant.m3u8
            """;

        HlsVariant description = MasterPlaylist.Describe(playlist)!;

        Assert.Equal("High", description.VideoProfile);
        Assert.Equal(3.1, description.VideoLevel);
        Assert.Equal(1920, description.Width);
        Assert.Equal(1080, description.Height);
        Assert.Equal(2500000, description.BitsPerSecond);
    }

    [Fact]
    public void APlaylistWithNoVariantsDescribesNoVariant()
    {
        const string playlist = "#EXTM3U\n#EXT-X-ENDLIST\n";

        Assert.Null(MasterPlaylist.Describe(playlist));
    }

    [Fact]
    public void TheBestVariantIsTheHighestBandwidthOnesUri()
    {
        const string master = "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=598880\nlow/index.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=1008884\n\nhigh/index.m3u8\n";

        Assert.Equal("high/index.m3u8", MasterPlaylist.BestVariantUri(master));
    }

    [Theory]
    [InlineData("")]
    [InlineData("#EXTM3U\n#EXTINF:6,\n00.ts\n")]
    [InlineData("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1008884\n")]
    public void AMasterPlaylistWithNoVariantUriHasNoBestVariant(string master)
    {
        Assert.Null(MasterPlaylist.BestVariantUri(master));
    }

    /// <summary>
    /// Kan's master, as ffprobe numbered it on 2026-10-16: the audio rendition is stream 0, then
    /// one video stream per variant in listed order, so the 1080p variant listed last is stream 5.
    /// </summary>
    [Fact]
    public void ASeparateAudioRenditionIsMappedWithTheBestVariantsVideo()
    {
        HlsStreamIndices indices = MasterPlaylist.FfmpegStreamIndices(FixtureFile.ReadText("kan-master.m3u8"))!;

        Assert.Equal(new HlsStreamIndices(Video: 5, Audio: 0), indices);
    }

    [Fact]
    public void StreamsAreNumberedInTheOrderTheirPlaylistsAreListed()
    {
        const string master = """
            #EXTM3U
            #EXT-X-STREAM-INF:BANDWIDTH=500000,AUDIO="aac"
            low.m3u8
            #EXT-X-STREAM-INF:BANDWIDTH=900000,AUDIO="aac"
            high.m3u8
            #EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="aac",DEFAULT=YES,URI="audio.m3u8"
            """;

        Assert.Equal(new HlsStreamIndices(Video: 1, Audio: 2), MasterPlaylist.FfmpegStreamIndices(master));
    }

    /// <summary>A variant that carries its own audio is two streams, whose order ffmpeg alone knows:
    /// such a master is left to the default mapping.</summary>
    [Fact]
    public void VariantsWithTheirOwnAudioHaveNoMapping()
    {
        Assert.Null(MasterPlaylist.FfmpegStreamIndices(FixturePlaylist));
    }

    [Theory]
    [InlineData("""
        #EXTM3U
        #EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="aac",NAME="HEB",URI="heb.m3u8"
        #EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="aac",NAME="ENG",URI="eng.m3u8"
        #EXT-X-STREAM-INF:BANDWIDTH=900000,AUDIO="aac"
        video.m3u8
        """)]
    [InlineData("""
        #EXTM3U
        #EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="aac",URI="audio.m3u8"
        #EXT-X-MEDIA:TYPE=SUBTITLES,GROUP-ID="subs",URI="subs.m3u8"
        #EXT-X-STREAM-INF:BANDWIDTH=900000,AUDIO="aac"
        video.m3u8
        """)]
    [InlineData("""
        #EXTM3U
        #EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="aac",URI="audio.m3u8"
        #EXT-X-STREAM-INF:BANDWIDTH=500000,AUDIO="aac"
        video.m3u8
        #EXT-X-STREAM-INF:BANDWIDTH=900000,AUDIO="aac"
        video.m3u8
        """)]
    [InlineData("#EXTM3U\n#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"aac\",URI=\"audio.m3u8\"\n")]
    public void AnyOtherLayoutHasNoMapping(string master)
    {
        Assert.Null(MasterPlaylist.FfmpegStreamIndices(master));
    }
}
