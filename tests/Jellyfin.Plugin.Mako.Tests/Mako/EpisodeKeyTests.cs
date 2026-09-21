using Jellyfin.Plugin.Mako.MakoApi;

namespace Jellyfin.Plugin.Mako.Tests.Mako;

public class EpisodeKeyTests
{
    [Fact]
    public void ToStringJoinsThePartsWithATilde()
    {
        var key = new EpisodeKey("vcm-1", "chan-2", "gallery-3");

        Assert.Equal("vcm-1~chan-2~gallery-3", key.ToString());
    }

    [Fact]
    public void RoundTripsThroughToStringAndParse()
    {
        var key = new EpisodeKey("vcm-1", "chan-2", "gallery-3");

        EpisodeKey parsed = EpisodeKey.Parse(key.ToString());

        Assert.Equal(key, parsed);
    }

    [Fact]
    public void RejectsAnEpisodeIdWithOnlyTwoParts()
    {
        Assert.Throws<FormatException>(() => EpisodeKey.Parse("vcm-1~chan-2"));
    }

    [Fact]
    public void RejectsAnEpisodeIdWithAnEmptyPart()
    {
        Assert.Throws<FormatException>(() => EpisodeKey.Parse("vcm-1~~gallery-3"));
    }
}
