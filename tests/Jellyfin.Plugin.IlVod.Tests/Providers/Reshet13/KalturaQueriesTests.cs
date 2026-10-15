using Jellyfin.Plugin.IlVod.Providers.Reshet13.Source;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Reshet13;

public class KalturaQueriesTests
{
    private const string Injection = "57' or asset_type = '1262";

    [Fact]
    public void AnEpisodesPageOfANonIdIsRefusedNamingTheSeriesId()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => KalturaQueries.EpisodesPage(Injection, 1));

        Assert.Equal("seriesId", exception.ParamName);
    }

    [Fact]
    public void ASeasonAssetsPageOfANonIdIsRefusedNamingTheSeriesId()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => KalturaQueries.SeasonAssetsPage(Injection, 1));

        Assert.Equal("seriesId", exception.ParamName);
    }

    [Fact]
    public void AnAssetOfANonIdIsRefusedNamingTheAssetId()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => KalturaQueries.Asset(Injection));

        Assert.Equal("assetId", exception.ParamName);
    }

    [Fact]
    public void AWellFormedIdIsAskedAboutAsGiven()
    {
        Assert.Equal("series 57 episodes page 2", KalturaQueries.EpisodesPage("57", 2).Subject);
        Assert.Equal("2361602", KalturaQueries.Asset("2361602").Fields["id"]!.GetValue<string>());
    }
}
