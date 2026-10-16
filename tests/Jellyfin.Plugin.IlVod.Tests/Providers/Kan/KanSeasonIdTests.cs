using Jellyfin.Plugin.IlVod.Providers.Kan;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Kan;

public class KanSeasonIdTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(2021)]
    [InlineData(null)]
    public void ASeasonIdReadsBackAsTheSeasonItWasMadeFrom(int? season)
    {
        Assert.Equal(season, KanSeasonId.Parse(KanSeasonId.Of(season)));
    }

    /// <summary>Kan itself numbers the unnumbered episodes 0, and no real season is ever 0.</summary>
    [Fact]
    public void EpisodesWithNoSeasonShareTheIdZero()
    {
        Assert.Equal("0", KanSeasonId.Of(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("עונה 4")]
    public void AnIdThatIsNoSeasonNumberIsRefused(string seasonId)
    {
        Assert.Throws<FormatException>(() => KanSeasonId.Parse(seasonId));
    }
}
