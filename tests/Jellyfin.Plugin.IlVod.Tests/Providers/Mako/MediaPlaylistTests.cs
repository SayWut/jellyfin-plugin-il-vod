using Jellyfin.Plugin.IlVod.Providers.Mako.Source;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Mako;

public class MediaPlaylistTests
{
    [Fact]
    public void TheDurationIsTheSumOfEverySegment()
    {
        const string playlist = "#EXTM3U\n#EXT-X-TARGETDURATION:6\n#EXTINF:6.000000,\n00.ts\n#EXTINF:6.000000,\n01.ts\n#EXTINF:2.080000,\n02.ts\n#EXT-X-ENDLIST\n";

        Assert.Equal(TimeSpan.FromSeconds(14.08), MediaPlaylist.TotalDuration(playlist));
    }

    [Fact]
    public void ASegmentTitleAfterTheCommaAndWindowsLineEndingsAreTolerated()
    {
        const string playlist = "#EXTM3U\r\n#EXTINF:4.5,first part\r\n00.ts\r\n#EXTINF:3\r\n01.ts\r\n";

        Assert.Equal(TimeSpan.FromSeconds(7.5), MediaPlaylist.TotalDuration(playlist));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>not a playlist</html>")]
    [InlineData("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1\nvariant.m3u8\n")]
    [InlineData("#EXTM3U\n#EXTINF:oops,\n00.ts\n")]
    public void APlaylistWithNoMeasurableSegmentHasNoDuration(string playlist)
    {
        Assert.Null(MediaPlaylist.TotalDuration(playlist));
    }
}
