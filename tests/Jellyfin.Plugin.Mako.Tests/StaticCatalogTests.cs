using Jellyfin.Plugin.Mako.Catalog;
using Jellyfin.Plugin.Mako.Model;

namespace Jellyfin.Plugin.Mako.Tests;

public class StaticCatalogTests
{
    private const string PastedUrl = "https://example.test/master.m3u8?hdnea=token";
    private const string PastedContainer = "hls";

    private static StaticCatalog CatalogWithPastedUrl(string url = PastedUrl, string container = PastedContainer)
        => new(() => url, () => container);

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
    public async Task EveryEpisodeHasTheTwentyFiveMinuteTestDuration()
    {
        StaticCatalog catalog = CatalogWithPastedUrl();
        IReadOnlyList<Season> seasons = await catalog.GetSeasonsAsync(StaticCatalog.PlayableShowId, CancellationToken.None);

        IReadOnlyList<Episode> firstSeason = await catalog.GetEpisodesAsync(seasons[0].Id, CancellationToken.None);

        Assert.All(firstSeason, episode => Assert.Equal(TimeSpan.FromMinutes(25), episode.Duration));
    }

    [Fact]
    public async Task EveryEpisodePlaysThePastedUrlWithThePastedContainer()
    {
        StreamLink link = await CatalogWithPastedUrl(container: "hls").GetStreamLinkAsync("any-episode", CancellationToken.None);

        Assert.Equal(PastedUrl, link.Url);
        Assert.Equal("hls", link.Container);
    }

    [Fact]
    public async Task AnEmptyContainerSettingYieldsAnEmptyStreamLinkContainer()
    {
        StreamLink link = await CatalogWithPastedUrl(container: string.Empty).GetStreamLinkAsync("any-episode", CancellationToken.None);

        Assert.Equal(string.Empty, link.Container);
    }

    [Fact]
    public async Task UrlIsReadAtPlayTimeNotAtConstruction()
    {
        string current = "https://example.test/first.m3u8";
        var catalog = new StaticCatalog(() => current, () => "hls");

        current = "https://example.test/second.m3u8";
        StreamLink link = await catalog.GetStreamLinkAsync("any-episode", CancellationToken.None);

        Assert.Equal("https://example.test/second.m3u8", link.Url);
    }

    [Fact]
    public async Task ContainerIsReadAtPlayTimeNotAtConstruction()
    {
        string currentContainer = "hls";
        var catalog = new StaticCatalog(() => PastedUrl, () => currentContainer);

        currentContainer = string.Empty;
        StreamLink link = await catalog.GetStreamLinkAsync("any-episode", CancellationToken.None);

        Assert.Equal(string.Empty, link.Container);
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
