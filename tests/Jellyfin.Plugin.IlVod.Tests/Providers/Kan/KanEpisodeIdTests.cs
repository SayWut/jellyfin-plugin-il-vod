using Jellyfin.Plugin.IlVod.Providers.Kan;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Kan;

public class KanEpisodeIdTests
{
    [Fact]
    public void AnEpisodeIdNamesItsProgramAndItsEntry()
    {
        Assert.Equal("11510-1107310", KanEpisodeId.Of("11510", "1107310"));
    }

    [Fact]
    public void AnEpisodeIdReadsBackAsTheIdsItWasMadeFrom()
    {
        Assert.True(KanEpisodeId.TryParse(KanEpisodeId.Of("11510", "1107310"), out string? programId, out string? entryId));

        Assert.Equal(("11510", "1107310"), (programId, entryId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1107310")]
    [InlineData("11510-")]
    [InlineData("-1107310")]
    [InlineData("11510-1107310-1")]
    [InlineData("11510-abc")]
    [InlineData("../11510-1107310")]
    public void AnythingElseIsNoEpisodeId(string episodeId)
    {
        Assert.False(KanEpisodeId.TryParse(episodeId, out _, out _));
    }
}
