using Jellyfin.Plugin.IlVod.Providers.Reshet13;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Reshet13;

public class Reshet13ShowPageTests
{
    private static readonly Reshet13ShowPage ShowPage = new(new Dictionary<int, string> { [4] = "עונה 5" }, []);

    [Fact]
    public void ASeasonIndexWithASeasonAssetHasItsName() =>
        Assert.Equal("עונה 5", ShowPage.SeasonAssetNameOf(4));

    [Fact]
    public void ASeasonIndexWithoutASeasonAssetHasNoName() =>
        Assert.Null(ShowPage.SeasonAssetNameOf(7));

    [Fact]
    public void TheEpisodesWithNoSeasonIndexHaveNoSeasonAssetName() =>
        Assert.Null(ShowPage.SeasonAssetNameOf(null));
}
