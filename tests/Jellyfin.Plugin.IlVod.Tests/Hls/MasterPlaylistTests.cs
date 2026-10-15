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
}
