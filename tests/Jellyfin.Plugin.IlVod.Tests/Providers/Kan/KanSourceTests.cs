using Jellyfin.Plugin.IlVod.Cache;
using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Hls;
using Jellyfin.Plugin.IlVod.Providers.Kan;
using Jellyfin.Plugin.IlVod.Providers.Kan.Source;
using Jellyfin.Plugin.IlVod.Tests.Support;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Kan;

public class KanSourceTests
{
    private const string Drama = "דרמה";
    private const string Comedy = "קומדיה";
    private const string DramaSlug = "drama";
    private const string ComedySlug = "comedy";
    private const string FixtureDramaSlug = "%D7%93%D7%A8%D7%9E%D7%94";

    private const string SatireId = "11510";
    private const string SatireEpisodeId = "11510-1107310";
    private const string MasterUrl = KanPlayerConfigTests.MasterUrl;
    private const string BestVariantUrl =
        "https://r.il.cdn-redge.media/webcache/gorigin/hls/oil/kancdn/vod/2253468/LIBCODER_SMOOTH_1080_KAN/Manifest.ism/playlist.m3u8?id=1&bitrate=5500000&fmp4";

    private const string VariantPlaylist = "#EXTM3U\n#EXTINF:600.0,\na.mp4\n#EXTINF:42.5,\nb.mp4\n#EXT-X-ENDLIST\n";

    private static readonly KanLifetimes Lifetimes = new(Catalogue: TimeSpan.FromHours(24), Show: TimeSpan.FromHours(6));
    private static readonly TimeSpan PastEveryLifetime = TimeSpan.FromDays(2);
    private static readonly TimeSpan SatireEpisodeLength = new(0, 29, 35);

    private static readonly string SatireEpisodePage = KanUrls.EpisodePage(SatireId, "1107310");

    private readonly FakeClock _clock = new();

    // ---- the index ----

    [Fact]
    public async Task ACatalogueIsEveryGenrePageOfEveryRailInTheLobby()
    {
        FakeKanHttp http = FixtureCatalogueHttp();
        KanSource source = CreateSource(http);

        KanShowIndex index = await source.FetchIndexAsync(CancellationToken.None);

        Assert.Equal(40, index.Shows.Count);
        KanShowCard first = index.Shows[0];
        Assert.Equal(("13291", "שטוקהולם"), (first.ProgramId, first.Name));
        Assert.Equal("https://mobapi.kan.org.il/media/h5ih4k4g/1920x1080_.jpg", first.ImageUrl);
        Assert.Equal([Drama], first.Genres);
        Assert.Equal(1 + 2 + 23, http.Requests.Count);
    }

    [Fact]
    public async Task AShowTheLobbyShowsCarriesItsPosterAndSynopsis()
    {
        KanSource source = CreateSource(FixtureCatalogueHttp());

        KanShowIndex index = await source.FetchIndexAsync(CancellationToken.None);

        KanShowCard show = index.Shows.Single(show => show.ProgramId == "1033206");
        Assert.StartsWith("https://mobapi.kan.org.il/media/fd1h5wxu/poster-image_small_239x360_", show.PosterUrl, StringComparison.Ordinal);
        Assert.StartsWith("שלושה אחים", show.Overview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFreshIndexIsServedWithoutARequest()
    {
        FakeKanHttp http = SeededHttp();
        KanSource source = CreateSource(http);
        await source.FetchIndexAsync(CancellationToken.None);
        int requestsAfterFirst = http.Requests.Count;

        await source.FetchIndexAsync(CancellationToken.None);

        Assert.Equal(3, requestsAfterFirst);
        Assert.Equal(requestsAfterFirst, http.Requests.Count);
    }

    [Fact]
    public async Task AShowOnSeveralRailsIsListedOnceWhereFirstSeenWithEveryGenreInRailOrder()
    {
        KanSource source = CreateSource(SeededHttp());

        KanShowIndex index = await source.FetchIndexAsync(CancellationToken.None);

        Assert.Equal(["1", "2", "3"], index.Shows.Select(show => show.ProgramId));
        Assert.Equal([Drama, Comedy], index.Shows[0].Genres);
        Assert.Equal([Comedy], index.Shows[2].Genres);
        Assert.Equal("https://mobapi.kan.org.il/media/p/1.jpg", index.Shows[0].PosterUrl);
        Assert.Equal("סיפור", index.Shows[0].Overview);
        Assert.Null(index.Shows[1].PosterUrl);
    }

    [Fact]
    public async Task ACardRepeatedOnTheNextPageIsListedOnce()
    {
        FakeKanHttp http = HttpWithDramaPages(
            KanHtml.GenrePage(3, 2, KanHtml.Card("1"), KanHtml.Card("2")),
            KanHtml.GenrePage(3, 2, KanHtml.Card("2"), KanHtml.Card("3")));
        KanSource source = CreateSource(http);

        KanShowIndex index = await source.FetchIndexAsync(CancellationToken.None);

        Assert.Equal(["1", "2", "3"], index.Shows.Select(show => show.ProgramId));
    }

    /// <summary>A rail that comes up short is Kan's unstable sort shifting cards between pages
    /// mid-walk: it is walked once more, and if it is still short Kan is answering inconsistently
    /// right now — unavailable, never a short list, and never cached.</summary>
    [Fact]
    public async Task AWalkEndingShortTwiceIsRefusedAsUnavailableAndNeverCached()
    {
        FakeKanHttp http = HttpWithDramaPages(
            KanHtml.GenrePage(4, 2, KanHtml.Card("1"), KanHtml.Card("2")),
            KanHtml.GenrePage(4, 2, KanHtml.Card("2"), KanHtml.Card("3")));
        KanSource source = CreateSource(http);

        await Assert.ThrowsAsync<KanUnavailableException>(() => source.FetchIndexAsync(CancellationToken.None));
        await Assert.ThrowsAsync<KanUnavailableException>(() => source.FetchIndexAsync(CancellationToken.None));

        Assert.Equal(2, http.Requests.Count(url => url == KanUrls.Lobby));
        Assert.Equal(4, http.Requests.Count(url => url == KanUrls.GenrePage(DramaSlug, 1)));
    }

    [Fact]
    public async Task AWalkEndingShortServesTheStaleIndex()
    {
        FakeKanHttp http = SeededHttp();
        KanSource source = CreateSource(http);
        await source.FetchIndexAsync(CancellationToken.None);

        _clock.Advance(PastEveryLifetime);
        http.RespondTo(KanUrls.GenrePage(DramaSlug, 1), KanHtml.GenrePage(3, null, KanHtml.Card("1"), KanHtml.Card("2")));
        KanShowIndex stale = await source.FetchIndexAsync(CancellationToken.None);

        Assert.Equal(3, stale.Shows.Count);
    }

    [Fact]
    public async Task ShowsOutsideKan11AreLeftOutAfterTheWalkIsCounted()
    {
        FakeKanHttp http = HttpWithDramaPages(
            KanHtml.GenrePage(2, null, KanHtml.Card("1"), KanHtml.Card("9", KanHtml.ArchiveSection)));
        KanSource source = CreateSource(http);

        KanShowIndex index = await source.FetchIndexAsync(CancellationToken.None);

        Assert.Equal(["1"], index.Shows.Select(show => show.ProgramId));
    }

    [Fact]
    public async Task ACatalogueWithNoKan11ShowIsRefused()
    {
        FakeKanHttp http = HttpWithDramaPages(KanHtml.GenrePage(1, null, KanHtml.Card("9", KanHtml.ArchiveSection)));
        KanSource source = CreateSource(http);

        await Assert.ThrowsAsync<FormatException>(() => source.FetchIndexAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AFailureWithNothingCachedIsThrownAndNeverCached()
    {
        FakeKanHttp http = SeededHttp();
        http.Fail(KanUrls.GenrePage(ComedySlug, 1));
        KanSource source = CreateSource(http);

        await Assert.ThrowsAsync<KanUnavailableException>(() => source.FetchIndexAsync(CancellationToken.None));
        SeedCatalogue(http);
        KanShowIndex index = await source.FetchIndexAsync(CancellationToken.None);

        Assert.Equal(3, index.Shows.Count);
    }

    [Fact]
    public async Task AStaleIndexIsServedWhenKanFails()
    {
        FakeKanHttp http = SeededHttp();
        KanSource source = CreateSource(http);
        await source.FetchIndexAsync(CancellationToken.None);

        _clock.Advance(PastEveryLifetime);
        http.Fail(KanUrls.Lobby);
        KanShowIndex stale = await source.FetchIndexAsync(CancellationToken.None);

        Assert.Equal(3, stale.Shows.Count);
    }

    [Fact]
    public async Task AnExpiredIndexIsFetchedAgain()
    {
        FakeKanHttp http = SeededHttp();
        KanSource source = CreateSource(http);
        await source.FetchIndexAsync(CancellationToken.None);

        _clock.Advance(Lifetimes.Catalogue + TimeSpan.FromMinutes(1));
        await source.FetchIndexAsync(CancellationToken.None);

        Assert.Equal(2, http.Requests.Count(url => url == KanUrls.Lobby));
    }

    // ---- a show and its seasons ----

    [Fact]
    public async Task OpeningAShowReadsItsFeedOnce()
    {
        FakeKanHttp http = SeededHttp();
        KanSource source = CreateSource(http);

        KanShowPage page = await source.FetchShowAsync(SatireId, CancellationToken.None);
        await source.FetchShowAsync(SatireId, CancellationToken.None);

        Assert.Equal("מה שתגידו", page.ShowName);
        Assert.Equal([KanUrls.ProgramFeed(SatireId)], http.Requests);
    }

    [Theory]
    [InlineData("11510&id=1")]
    [InlineData("")]
    public async Task AShowIdThatIsNotDigitsIsRefusedBeforeAnyRequest(string showId)
    {
        FakeKanHttp http = SeededHttp();
        KanSource source = CreateSource(http);

        await Assert.ThrowsAsync<ArgumentException>(() => source.FetchShowAsync(showId, CancellationToken.None));

        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task AnUnknownProgramIsRefusedAndNeverCached()
    {
        FakeKanHttp http = new();
        http.RespondTo(KanUrls.ProgramFeed("5"), """{"title":"","entry":[]}""");
        KanSource source = CreateSource(http);

        await Assert.ThrowsAsync<FormatException>(() => source.FetchShowAsync("5", CancellationToken.None));
        await Assert.ThrowsAsync<FormatException>(() => source.FetchShowAsync("5", CancellationToken.None));

        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public async Task AStaleShowIsServedWhenKanFails()
    {
        FakeKanHttp http = SeededHttp();
        KanSource source = CreateSource(http);
        await source.FetchShowAsync(SatireId, CancellationToken.None);

        _clock.Advance(PastEveryLifetime);
        http.Fail(KanUrls.ProgramFeed(SatireId));
        KanShowPage stale = await source.FetchShowAsync(SatireId, CancellationToken.None);

        Assert.Equal(102, stale.Episodes.Count);
    }

    [Fact]
    public async Task OpeningASeasonOfAFreshShowMakesNoRequest()
    {
        FakeKanHttp http = SeededHttp();
        KanSource source = CreateSource(http);
        await source.FetchShowAsync(SatireId, CancellationToken.None);
        int requestsBefore = http.Requests.Count;

        await source.FetchSeasonAsync(SatireId, "6", CancellationToken.None);

        Assert.Equal(requestsBefore, http.Requests.Count);
    }

    [Fact]
    public async Task ASeasonIsTheShowsEpisodesWithItsNumber()
    {
        KanSource source = CreateSource(SeededHttp());

        KanSeasonPage season = await source.FetchSeasonAsync(SatireId, "6", CancellationToken.None);

        Assert.Equal(["1107310", "1107309"], season.Episodes.Select(episode => episode.EntryId));
        Assert.Equal(6, season.Season);
        Assert.Equal("מה שתגידו", season.ShowName);
        Assert.True(season.ShowHasNumberedSeasons);
    }

    [Fact]
    public async Task TheUnnumberedSeasonIsTheEpisodesThatStateNone()
    {
        KanSource source = CreateSource(SeededHttp());

        KanSeasonPage season = await source.FetchSeasonAsync(SatireId, KanSeasonId.NoSeason, CancellationToken.None);

        Assert.Null(season.Season);
        Assert.Equal(5, season.Episodes.Count);
    }

    [Fact]
    public async Task OpeningASeasonNeverFetchesTheIndex()
    {
        FakeKanHttp http = SeededHttp();
        KanSource source = CreateSource(http);

        await source.FetchSeasonAsync(SatireId, "6", CancellationToken.None);

        Assert.Equal([KanUrls.ProgramFeed(SatireId)], http.Requests);
    }

    // ---- play ----

    [Fact]
    public async Task PlayingAnEpisodeOfAnOpenedShowReadsItsPageAndMasterAndPlaysTheMaster()
    {
        FakeKanHttp http = SeededHttp();
        KanSource source = CreateSource(http);
        await source.FetchShowAsync(SatireId, CancellationToken.None);
        int requestsBefore = http.Requests.Count;

        KanStream stream = await source.FetchStreamAsync(SatireEpisodeId, CancellationToken.None);

        Assert.Equal([SatireEpisodePage, MasterUrl], http.Requests.Skip(requestsBefore));
        Assert.Equal(MasterUrl, stream.Url);
        Assert.Equal(new HlsStreamIndices(Video: 5, Audio: 0), stream.StreamIndices);
        Assert.Equal(1920, stream.Variant?.Width);
        Assert.Equal(1080, stream.Variant?.Height);
        Assert.Equal(SatireEpisodeLength, stream.Duration);
    }

    [Fact]
    public async Task PlayingAnEpisodeOfACachedShowTakesItsLengthFromTheCacheWithoutReadingTheFeed()
    {
        TimedCache cache = new(_clock);
        await CreateSource(SeededHttp(), cache).FetchShowAsync(SatireId, CancellationToken.None);
        FakeKanHttp http = SeededHttp();

        KanStream stream = await CreateSource(http, cache).FetchStreamAsync(SatireEpisodeId, CancellationToken.None);

        Assert.Equal([SatireEpisodePage, MasterUrl], http.Requests);
        Assert.Equal(SatireEpisodeLength, stream.Duration);
    }

    [Fact]
    public async Task PlayingAnEpisodeOfNoKnownShowMeasuresItsLengthFromTheBestVariant()
    {
        FakeKanHttp http = SeededHttp();
        KanSource source = CreateSource(http);

        KanStream stream = await source.FetchStreamAsync(SatireEpisodeId, CancellationToken.None);

        Assert.Equal([SatireEpisodePage, MasterUrl, BestVariantUrl], http.Requests);
        Assert.Equal(TimeSpan.FromSeconds(642.5), stream.Duration);
        Assert.Equal(MasterUrl, stream.Url);
    }

    [Fact]
    public async Task AVariantThatCannotBeReadLeavesTheLengthUnstated()
    {
        FakeKanHttp http = SeededHttp();
        http.Fail(BestVariantUrl);
        RecordingLogger<KanSource> logger = new();
        KanSource source = CreateSource(http, new TimedCache(_clock), logger);

        KanStream stream = await source.FetchStreamAsync(SatireEpisodeId, CancellationToken.None);

        Assert.Null(stream.Duration);
        Assert.Equal(1920, stream.Variant?.Width);
        Assert.Single(logger.Warnings);
    }

    [Fact]
    public async Task AMasterThatCannotBeReadStillPlaysUndescribed()
    {
        FakeKanHttp http = SeededHttp();
        http.Fail(MasterUrl);
        RecordingLogger<KanSource> logger = new();
        KanSource source = CreateSource(http, new TimedCache(_clock), logger);
        await source.FetchShowAsync(SatireId, CancellationToken.None);

        KanStream stream = await source.FetchStreamAsync(SatireEpisodeId, CancellationToken.None);

        Assert.Equal(MasterUrl, stream.Url);
        Assert.Null(stream.Variant);
        Assert.Null(stream.StreamIndices);
        Assert.Equal(SatireEpisodeLength, stream.Duration);
        string warning = Assert.Single(logger.Warnings);
        Assert.DoesNotContain("fmp4", warning, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1107310")]
    [InlineData("11510-1107310/../x")]
    [InlineData("")]
    public async Task AnEpisodeIdThatIsNotTwoNumbersIsRefusedBeforeAnyRequest(string episodeId)
    {
        FakeKanHttp http = SeededHttp();
        KanSource source = CreateSource(http);

        await Assert.ThrowsAsync<StreamUnavailableException>(() => source.FetchStreamAsync(episodeId, CancellationToken.None));

        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task APageWithNoPlayerIsNotPlayable()
    {
        FakeKanHttp http = new();
        http.RespondTo(SatireEpisodePage, "<html>no player</html>");
        KanSource source = CreateSource(http);

        await Assert.ThrowsAsync<StreamUnavailableException>(() => source.FetchStreamAsync(SatireEpisodeId, CancellationToken.None));

        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task APlayerOnAnotherHostIsNotPlayed()
    {
        FakeKanHttp http = new();
        http.RespondTo(
            SatireEpisodePage,
            """<script type="application/json" data-redge-config="p">{"id":"1107310","file":{"hls":"//evil.example/a.m3u8"}}</script>""");
        KanSource source = CreateSource(http);

        await Assert.ThrowsAsync<StreamUnavailableException>(() => source.FetchStreamAsync(SatireEpisodeId, CancellationToken.None));

        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task AnEpisodePageKanCannotServeIsUnavailable()
    {
        FakeKanHttp http = SeededHttp();
        http.Fail(SatireEpisodePage);
        KanSource source = CreateSource(http);

        await Assert.ThrowsAsync<KanUnavailableException>(() => source.FetchStreamAsync(SatireEpisodeId, CancellationToken.None));
    }

    [Fact]
    public async Task PlayNeverFetchesTheIndexOrAFeed()
    {
        FakeKanHttp http = SeededHttp();
        KanSource source = CreateSource(http);

        await source.FetchStreamAsync(SatireEpisodeId, CancellationToken.None);

        Assert.DoesNotContain(KanUrls.Lobby, http.Requests);
        Assert.DoesNotContain(KanUrls.ProgramFeed(SatireId), http.Requests);
    }

    // ---- seeding ----

    private KanSource CreateSource(FakeKanHttp http) => CreateSource(http, new TimedCache(_clock));

    private static KanSource CreateSource(FakeKanHttp http, TimedCache cache) =>
        CreateSource(http, cache, new RecordingLogger<KanSource>());

    private static KanSource CreateSource(FakeKanHttp http, TimedCache cache, RecordingLogger<KanSource> logger) =>
        new(http, cache, () => Lifetimes, logger);

    /// <summary>A two-rail catalogue (program 1 on both), the satire feed, its episode page, master and best variant.</summary>
    private static FakeKanHttp SeededHttp()
    {
        FakeKanHttp http = new();
        SeedCatalogue(http);
        http.RespondTo(KanUrls.ProgramFeed(SatireId), FixtureFile.ReadText("kan-program-11510.json"));
        http.RespondTo(SatireEpisodePage, FixtureFile.ReadText("kan-episode-page.html"));
        http.RespondTo(MasterUrl, FixtureFile.ReadText("kan-master.m3u8"));
        http.RespondTo(BestVariantUrl, VariantPlaylist);
        return http;
    }

    private static void SeedCatalogue(FakeKanHttp http)
    {
        http.RespondTo(KanUrls.Lobby, KanHtml.Lobby((Drama, DramaSlug), (Comedy, ComedySlug)) + KanHtml.LobbyCard("1", "/media/p/1.jpg", "סיפור"));
        http.RespondTo(KanUrls.GenrePage(DramaSlug, 1), KanHtml.GenrePage(2, null, KanHtml.Card("1"), KanHtml.Card("2")));
        http.RespondTo(KanUrls.GenrePage(ComedySlug, 1), KanHtml.GenrePage(2, null, KanHtml.Card("3"), KanHtml.Card("1")));
    }

    private static FakeKanHttp HttpWithDramaPages(params string[] pages)
    {
        FakeKanHttp http = new();
        http.RespondTo(KanUrls.Lobby, KanHtml.Lobby((Drama, DramaSlug)));
        for (int page = 1; page <= pages.Length; page++)
        {
            http.RespondTo(KanUrls.GenrePage(DramaSlug, page), pages[page - 1]);
        }

        return http;
    }

    /// <summary>The recorded lobby, the recorded drama pages, and every other rail empty.</summary>
    private static FakeKanHttp FixtureCatalogueHttp()
    {
        FakeKanHttp http = new();
        string lobbyHtml = FixtureFile.ReadText("kan-lobby.html");
        http.RespondTo(KanUrls.Lobby, lobbyHtml);
        foreach (KanRail rail in KanPages.ParseLobby(lobbyHtml).Rails.Where(rail => rail.Slug != FixtureDramaSlug))
        {
            http.RespondTo(KanUrls.GenrePage(rail.Slug, 1), KanHtml.GenrePage(0, null));
        }

        http.RespondTo(KanUrls.GenrePage(FixtureDramaSlug, 1), FixtureFile.ReadText("kan-genre-drama-1.html"));
        http.RespondTo(KanUrls.GenrePage(FixtureDramaSlug, 2), FixtureFile.ReadText("kan-genre-drama-2.html"));
        return http;
    }
}
