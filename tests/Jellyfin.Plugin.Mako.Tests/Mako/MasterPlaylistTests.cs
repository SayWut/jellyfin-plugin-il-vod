using Jellyfin.Plugin.Mako.MakoApi;
using Jellyfin.Plugin.Mako.Model;
using Jellyfin.Plugin.Mako.Tests.Support;

namespace Jellyfin.Plugin.Mako.Tests.Mako;

public class MasterPlaylistTests
{
    private static readonly string FixturePlaylist = FixtureFile.ReadText("master.m3u8");

    [Fact]
    public void PicksTheHighestBandwidthVariant()
    {
        StreamDescription description = MasterPlaylist.Describe(FixturePlaylist);

        Assert.Equal(576, description.Width);
        Assert.Equal(432, description.Height);
        Assert.Equal(1008884, description.BitsPerSecond);
    }

    [Fact]
    public void FallsBackToMainWhenThereAreNoCodecs()
    {
        StreamDescription description = MasterPlaylist.Describe(FixturePlaylist);

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

        StreamDescription description = MasterPlaylist.Describe(playlist);

        Assert.Equal("High", description.VideoProfile);
        Assert.Equal(3.1, description.VideoLevel);
        Assert.Equal(1920, description.Width);
        Assert.Equal(1080, description.Height);
        Assert.Equal(2500000, description.BitsPerSecond);
    }

    [Fact]
    public void APlaylistWithNoVariantsGetsTheMakoDefault()
    {
        const string playlist = "#EXTM3U\n#EXT-X-ENDLIST\n";

        StreamDescription description = MasterPlaylist.Describe(playlist);

        Assert.Equal(StreamDescription.MakoDefault, description);
    }
}
