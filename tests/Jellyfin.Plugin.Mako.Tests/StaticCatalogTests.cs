using Jellyfin.Plugin.Mako.Catalog;
using Jellyfin.Plugin.Mako.Model;

namespace Jellyfin.Plugin.Mako.Tests;

public class StaticCatalogTests
{
    private const string PastedUrl = "https://example.test/master.m3u8?hdnea=token";

    private static StaticCatalog CatalogWithPastedUrl(string url = PastedUrl) => new(() => url);

    [Fact]
    public async Task OffersTwoShowsWithDifferentGenres()
    {
        IReadOnlyList<Show> shows = await CatalogWithPastedUrl().GetShowsAsync(CancellationToken.None);

        Assert.Equal(2, shows.Count);
        Assert.NotEqual(shows[0].Genres[0], shows[1].Genres[0]);
        Assert.All(shows, show => Assert.False(string.IsNullOrEmpty(show.PosterUrl)));
    }

    [Fact]
    public async Task PlayableShowHasTwoSeasonsNumberedOneAndTwo()
    {
        IReadOnlyList<Season> seasons = await CatalogWithPastedUrl()
            .GetSeasonsAsync(StaticCatalog.PlayableShowId, CancellationToken.None);

        Assert.Equal(new[] { 1, 2 }, seasons.Select(season => season.Number));
    }

    [Fact]
    public async Task CardOnlyShowHasNoSeasons()
    {
        IReadOnlyList<Season> seasons = await CatalogWithPastedUrl()
            .GetSeasonsAsync(StaticCatalog.CardOnlyShowId, CancellationToken.None);

        Assert.Empty(seasons);
    }

    [Fact]
    public async Task EachSeasonListsItsOwnEpisodesInOrder()
    {
        StaticCatalog catalog = CatalogWithPastedUrl();
        IReadOnlyList<Season> seasons = await catalog.GetSeasonsAsync(StaticCatalog.PlayableShowId, CancellationToken.None);

        IReadOnlyList<Episode> secondSeason = await catalog.GetEpisodesAsync(seasons[1].Id, CancellationToken.None);

        Assert.Equal(3, secondSeason.Count);
        Assert.All(secondSeason, episode => Assert.Equal(2, episode.SeasonNumber));
        Assert.Equal(new[] { 1, 2, 3 }, secondSeason.Select(episode => episode.Number));
    }

    [Fact]
    public async Task UnknownSeasonHasNoEpisodes()
    {
        IReadOnlyList<Episode> episodes = await CatalogWithPastedUrl()
            .GetEpisodesAsync("no-such-season", CancellationToken.None);

        Assert.Empty(episodes);
    }

    [Fact]
    public async Task EveryEpisodePlaysThePastedUrlAsHls()
    {
        StreamLink link = await CatalogWithPastedUrl().GetStreamLinkAsync("any-episode", CancellationToken.None);

        Assert.Equal(PastedUrl, link.Url);
        Assert.Equal("hls", link.Container);
    }

    [Fact]
    public async Task UrlIsReadAtPlayTimeNotAtConstruction()
    {
        string current = "https://example.test/first.m3u8";
        var catalog = new StaticCatalog(() => current);

        current = "https://example.test/second.m3u8";
        StreamLink link = await catalog.GetStreamLinkAsync("any-episode", CancellationToken.None);

        Assert.Equal("https://example.test/second.m3u8", link.Url);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PlayingWithNoPastedUrlFailsWithAReason(string emptyUrl)
    {
        StaticCatalog catalog = CatalogWithPastedUrl(emptyUrl);

        StreamUnavailableException error = await Assert.ThrowsAsync<StreamUnavailableException>(
            () => catalog.GetStreamLinkAsync("any-episode", CancellationToken.None));

        Assert.Contains("Test stream URL", error.Message, StringComparison.Ordinal);
    }
}
