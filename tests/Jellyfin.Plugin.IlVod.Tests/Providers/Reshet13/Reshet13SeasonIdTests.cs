using Jellyfin.Plugin.IlVod.Providers.Reshet13;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Reshet13;

public class Reshet13SeasonIdTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(10)]
    [InlineData(null)]
    public void ASeasonIdReadsBackAsTheIndexItWasMadeFrom(int? seasonIndex)
    {
        Assert.Equal(seasonIndex, Reshet13SeasonId.Parse(Reshet13SeasonId.Of(seasonIndex)));
    }

    [Fact]
    public void EpisodesWithNoSeasonShareOneIdThatIsNotANumber()
    {
        Assert.Equal(Reshet13SeasonId.NoSeason, Reshet13SeasonId.Of(null));
        Assert.False(int.TryParse(Reshet13SeasonId.NoSeason, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("עונה 8")]
    public void AnIdThatIsNoSeasonIndexIsRefused(string seasonId)
    {
        Assert.Throws<FormatException>(() => Reshet13SeasonId.Parse(seasonId));
    }
}
