using Jellyfin.Plugin.Mako.Cache;
using Jellyfin.Plugin.Mako.Catalog;
using Jellyfin.Plugin.Mako.MakoApi;
using Jellyfin.Plugin.Mako.Model;
using Jellyfin.Plugin.Mako.Tests.Support;

namespace Jellyfin.Plugin.Mako.Tests.Catalog;

public class MakoCatalogTests
{
    private const string AppVersion = "43.0";
    private const string PinnedPagePath = "/mako-vod-keshet/shemesh-s6";
    private const string PinnedShowName = "שמש";
    private const string SeasonOnePagePath = "/mako-vod-keshet/shemesh-s1";
    private const string UnknownShowPagePath = "/mako-vod-keshet/some-other-show";
    private const string NewPlaylistKey = "TESTKEY-PLAYLIST-NEW-24b";
    private const string TokenKey = "TESTKEY-TOKEN-TICKET-24b";
    private const string MediaUrl = "https://cdn.example.test/master.m3u8";
    private const string DecodedTicket = "hdnea=st=1~exp=2~acl=/*~hmac=ab";

    private static readonly PinnedShow Pinned = new(PinnedPagePath, PinnedShowName);

    private static readonly string MainConfigJson = FixtureFile.ReadText("main-config.json");
    private static readonly string CoreConfigJson = FixtureFile.ReadText("core-config.json");
    private static readonly string NotesConfigJson = FixtureFile.ReadText("notes-config.json");
    private static readonly string ShowPageJson = FixtureFile.ReadText("show-page-shemesh.json");
    private static readonly string SeasonOnePageJson = FixtureFile.ReadText("season-page-shemesh-s1.json");
    private static readonly string MasterPlaylistText = FixtureFile.ReadText("master.m3u8");

    private static readonly string MainConfigUrl = MakoUrls.MainConfigUrl(AppVersion);
    private static readonly string CoreConfigUrl = MakoConfig.CoreConfigUrlOf(MainConfigJson);
    private static readonly string NotesConfigUrl = MakoConfig.NotesUrlOf(CoreConfigJson);
    private static readonly MakoConfig ParsedConfig = MakoConfig.Parse(MainConfigJson, CoreConfigJson, NotesConfigJson);

    private static readonly string ShowPageUrl = MakoUrls.Page(ParsedConfig.BaseUrlTemplate, PinnedPagePath);
    private static readonly string SeasonOneUrl = MakoUrls.Page(ParsedConfig.BaseUrlTemplate, SeasonOnePagePath);

    private static readonly EpisodeKey Episode = new("vcm1", "channel1", "gallery1");
    private static readonly string PlaylistUrl =
        MakoUrls.Playlist(ParsedConfig.PlaylistUrlTemplate, Episode.VcmId, Episode.ChannelId, Episode.GalleryChannelId);
    private static readonly string TokenUrl = ParsedConfig.TokenUrl;
    private static readonly string FinalMediaUrl = $"{MediaUrl}?{DecodedTicket}";

    [Fact]
    public async Task TheChannelListsThePinnedShowWithoutAskingMako()
    {
        FakeMakoHttp http = new();
        MakoCatalog catalog = await CreateCatalogAsync(http);

        IReadOnlyList<Show> shows = await catalog.GetShowsAsync(CancellationToken.None);

        Show show = Assert.Single(shows);
        Assert.Equal(PinnedPagePath, show.Id);
        Assert.Equal(PinnedShowName, show.Name);
        Assert.Null(show.Overview);
        Assert.Null(show.PosterUrl);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task AfterTheShowWasOpenedItsCardGainsOverviewAndPoster()
    {
        FakeMakoHttp http = new();
        http.Respond(ShowPageUrl, ShowPageJson);
        MakoCatalog catalog = await CreateCatalogAsync(http);
        await catalog.GetSeasonsAsync(PinnedPagePath, CancellationToken.None);
        int requestsBeforeListing = http.Requests.Count;

        IReadOnlyList<Show> shows = await catalog.GetShowsAsync(CancellationToken.None);

        ShowPage expected = MakoPages.ParseShowPage(ShowPageJson);
        Show show = Assert.Single(shows);
        Assert.Equal(expected.Overview, show.Overview);
        Assert.Equal(expected.PosterUrl, show.PosterUrl);
        Assert.Equal(requestsBeforeListing, http.Requests.Count);
    }

    [Fact]
    public async Task OpeningTheShowListsItsSixSeasons()
    {
        FakeMakoHttp http = new();
        http.Respond(ShowPageUrl, ShowPageJson);
        MakoCatalog catalog = await CreateCatalogAsync(http);

        IReadOnlyList<Season> seasons = await catalog.GetSeasonsAsync(PinnedPagePath, CancellationToken.None);

        Assert.Equal(6, seasons.Count);
        Assert.Contains(seasons, season => season.Id == SeasonOnePagePath && season.Number == 1);
        Assert.Contains(seasons, season => season.Id == PinnedPagePath && season.Number == 6);
    }

    [Fact]
    public async Task OpeningAPastSeasonListsItsEpisodesWithIdsThatCarryPlaybackKeys()
    {
        FakeMakoHttp http = new();
        http.Respond(ShowPageUrl, ShowPageJson);
        http.Respond(SeasonOneUrl, SeasonOnePageJson);
        MakoCatalog catalog = await CreateCatalogAsync(http);
        await catalog.GetSeasonsAsync(PinnedPagePath, CancellationToken.None);

        IReadOnlyList<Episode> episodes = await catalog.GetEpisodesAsync(SeasonOnePagePath, CancellationToken.None);

        Episode first = Assert.Single(episodes, episode => episode.Number == 1);
        EpisodeKey key = EpisodeKey.Parse(first.Id);
        Assert.NotEmpty(key.VcmId);
        Assert.Equal(1, first.SeasonNumber);
        Assert.Equal(PinnedShowName, first.ShowName);
    }

    [Fact]
    public async Task AnUnknownShowHasNoSeasonsAndCostsNothing()
    {
        FakeMakoHttp http = new();
        MakoCatalog catalog = await CreateCatalogAsync(http);

        IReadOnlyList<Season> seasons = await catalog.GetSeasonsAsync(UnknownShowPagePath, CancellationToken.None);

        Assert.Empty(seasons);
        Assert.Empty(http.Requests);
    }

    /// <summary>
    /// The pinned page and the season it displays are DIFFERENT cache keys here (unlike the real
    /// fixtures above, where the pinned path happens to equal the current season's own path) — so
    /// this is a genuine test of <c>Remember</c> writing under a distinct key, not a same-key
    /// overwrite that would pass even if nothing were remembered at all.
    /// </summary>
    [Fact]
    public async Task TheDisplayedSeasonIsRememberedUnderItsOwnPathSoOpeningItCostsNothing()
    {
        const string showPath = "/x/show-latest";
        const string latestSeasonPath = "/x/show-latest-s3";
        string showJson = InlineShowPageJson(showPath, seasonCount: 3, displayedSeasonNumber: 3, episodeTitle: "S3 Episode");
        FakeMakoHttp http = new();
        http.Respond(MakoUrls.Page(ParsedConfig.BaseUrlTemplate, showPath), showJson);
        MakoCatalog catalog = await CreateCatalogAsync(http, pinnedShow: new PinnedShow(showPath, "Show"));

        await catalog.GetSeasonsAsync(showPath, CancellationToken.None);
        int requestsAfterSeasons = http.Requests.Count;

        IReadOnlyList<Episode> episodes = await catalog.GetEpisodesAsync(latestSeasonPath, CancellationToken.None);

        Assert.Equal(requestsAfterSeasons, http.Requests.Count);
        Assert.Equal("S3 Episode", Assert.Single(episodes).Name);
    }

    /// <summary>
    /// The wrong-season guard (finding 3): when the pinned page displays an EARLIER season than
    /// the latest, opening that displayed season is still free (remembered), but opening the
    /// actual latest season costs a request and returns THAT season's own episodes — proving the
    /// two are never confused with one another.
    /// </summary>
    [Fact]
    public async Task ADisplayedSeasonThatIsNotTheLatestIsFreeWhileTheLatestStillCostsARequestWithItsOwnEpisodes()
    {
        const string showPath = "/x/show-past";
        const string displayedSeasonPath = "/x/show-past-s1";
        const string latestSeasonPath = "/x/show-past-s3";
        string showJson = InlineShowPageJson(showPath, seasonCount: 3, displayedSeasonNumber: 1, episodeTitle: "S1 Episode");
        string latestSeasonJson = InlineSeasonPageJson(latestSeasonPath, episodeTitle: "S3 Episode");
        FakeMakoHttp http = new();
        http.Respond(MakoUrls.Page(ParsedConfig.BaseUrlTemplate, showPath), showJson);
        http.Respond(MakoUrls.Page(ParsedConfig.BaseUrlTemplate, latestSeasonPath), latestSeasonJson);
        MakoCatalog catalog = await CreateCatalogAsync(http, pinnedShow: new PinnedShow(showPath, "Show"));

        await catalog.GetSeasonsAsync(showPath, CancellationToken.None);
        int requestsAfterSeasons = http.Requests.Count;

        IReadOnlyList<Episode> displayedEpisodes = await catalog.GetEpisodesAsync(displayedSeasonPath, CancellationToken.None);
        Assert.Equal(requestsAfterSeasons, http.Requests.Count);
        Assert.Equal("S1 Episode", Assert.Single(displayedEpisodes).Name);

        IReadOnlyList<Episode> latestEpisodes = await catalog.GetEpisodesAsync(latestSeasonPath, CancellationToken.None);
        Assert.Equal(requestsAfterSeasons + 1, http.Requests.Count);
        Assert.Equal("S3 Episode", Assert.Single(latestEpisodes).Name);
    }

    [Fact]
    public async Task APastSeasonPageIsNotRefetchedJustAfterSixHoursButIsAfterSevenDays()
    {
        FakeClock clock = new();
        FakeMakoHttp http = new();
        http.Respond(SeasonOneUrl, SeasonOnePageJson);
        MakoCatalog catalog = await CreateCatalogAsync(http, clock);

        await catalog.GetEpisodesAsync(SeasonOnePagePath, CancellationToken.None);
        int requestsAfterFirst = http.Requests.Count;

        clock.Advance(TimeSpan.FromHours(6) + TimeSpan.FromMinutes(1));
        await catalog.GetEpisodesAsync(SeasonOnePagePath, CancellationToken.None);
        Assert.Equal(requestsAfterFirst, http.Requests.Count);

        clock.Advance(TimeSpan.FromDays(7));
        await catalog.GetEpisodesAsync(SeasonOnePagePath, CancellationToken.None);
        Assert.Equal(requestsAfterFirst + 1, http.Requests.Count);
    }

    /// <summary>
    /// The real fixture's own pinned path IS its displayed season's id (both
    /// <c>/mako-vod-keshet/shemesh-s6</c>) — the exact case where a naive, unconditional
    /// <c>Remember</c> would overwrite the show page's own 12h entry with the season's 6h one,
    /// silently downgrading it. The guard must skip the remember when the two ids already
    /// coincide, so the show page keeps its full 12h lifetime.
    /// </summary>
    [Fact]
    public async Task TheShowPageIsNotDowngradedToTheCurrentSeasonLifetimeWhenItsOwnPathIsTheDisplayedSeason()
    {
        FakeClock clock = new();
        FakeMakoHttp http = new();
        http.Respond(ShowPageUrl, ShowPageJson);
        MakoCatalog catalog = await CreateCatalogAsync(http, clock);

        await catalog.GetSeasonsAsync(PinnedPagePath, CancellationToken.None);
        int requestsAfterFirst = http.Requests.Count;

        // Past the 6h "current season" lifetime, but still within the show page's own 12h one.
        clock.Advance(TimeSpan.FromHours(6) + TimeSpan.FromMinutes(1));
        await catalog.GetSeasonsAsync(PinnedPagePath, CancellationToken.None);

        Assert.Equal(requestsAfterFirst, http.Requests.Count);
    }

    [Fact]
    public async Task TheLatestSeasonPageIsRefetchedAfterSixHoursWhenOpenedDirectly()
    {
        FakeClock clock = new();
        FakeMakoHttp http = new();
        http.Respond(ShowPageUrl, ShowPageJson);
        MakoCatalog catalog = await CreateCatalogAsync(http, clock);

        // Opened directly through GetEpisodesAsync, never through GetSeasonsAsync, so this is a
        // fresh cache entry governed purely by the "is this the latest season" 6h lifetime.
        await catalog.GetEpisodesAsync(PinnedPagePath, CancellationToken.None);
        int requestsAfterFirst = http.Requests.Count;

        clock.Advance(TimeSpan.FromHours(6));
        await catalog.GetEpisodesAsync(PinnedPagePath, CancellationToken.None);

        Assert.Equal(requestsAfterFirst + 1, http.Requests.Count);
    }

    [Fact]
    public async Task TheShowPageSurvivesTwelveHoursIndependentlyOfTheRememberedLatestSeason()
    {
        const string showPath = "/x/show-lifetime";
        string showJson = InlineShowPageJson(showPath, seasonCount: 3, displayedSeasonNumber: 3, episodeTitle: "S3 Episode");
        FakeClock clock = new();
        FakeMakoHttp http = new();
        http.Respond(MakoUrls.Page(ParsedConfig.BaseUrlTemplate, showPath), showJson);
        MakoCatalog catalog = await CreateCatalogAsync(http, clock, pinnedShow: new PinnedShow(showPath, "Show"));

        await catalog.GetSeasonsAsync(showPath, CancellationToken.None);
        int requestsAfterFirst = http.Requests.Count;

        // Past the remembered latest-season entry's own 6h lifetime, but the show page's key is
        // a separate cache entry with its own 12h lifetime — unaffected.
        clock.Advance(TimeSpan.FromHours(6) + TimeSpan.FromMinutes(1));
        await catalog.GetSeasonsAsync(showPath, CancellationToken.None);
        Assert.Equal(requestsAfterFirst, http.Requests.Count);

        clock.Advance(TimeSpan.FromHours(12));
        await catalog.GetSeasonsAsync(showPath, CancellationToken.None);
        Assert.Equal(requestsAfterFirst + 1, http.Requests.Count);
    }

    [Fact]
    public async Task PlayingResolvesOnceAndReusesTheLinkForFiveMinutes()
    {
        FakeClock clock = new();
        FakeMakoHttp http = new();
        http.Respond(PlaylistUrl, EncryptedPlaylist(isProtected: false));
        http.Respond(MediaUrl, MasterPlaylistText);
        MakoCatalog catalog = await CreateCatalogAsync(http, clock);

        StreamLink first = await catalog.GetStreamLinkAsync(Episode.ToString(), CancellationToken.None);
        int requestsAfterFirstResolve = http.Requests.Count;

        clock.Advance(TimeSpan.FromMinutes(4));
        StreamLink stillCached = await catalog.GetStreamLinkAsync(Episode.ToString(), CancellationToken.None);

        Assert.Equal(first, stillCached);
        Assert.Equal(requestsAfterFirstResolve, http.Requests.Count);

        clock.Advance(TimeSpan.FromMinutes(2));
        StreamLink resolvedAgain = await catalog.GetStreamLinkAsync(Episode.ToString(), CancellationToken.None);

        // A fresh resolve costs exactly the playlist GET and the master-playlist GET (config is
        // already cached) — never a loose "some new requests happened".
        Assert.Equal(requestsAfterFirstResolve + 2, http.Requests.Count);
        Assert.Equal(first.Url, resolvedAgain.Url);
    }

    /// <summary>
    /// N3: a signed URL must never reach disk. The browse page cache here is given a real file to
    /// persist to (unlike every other test's memory-only cache) so this is a genuine read of what
    /// landed on disk after browsing the show and then playing a protected episode, not an
    /// assumption from reading the production code.
    /// </summary>
    [Fact]
    public async Task NoSignedUrlEverReachesThePersistedBrowseCacheFile()
    {
        string persistencePath = Path.Combine(Path.GetTempPath(), $"mako-catalog-tests-browse-cache-{Guid.NewGuid():N}.json");
        try
        {
            FakeMakoHttp http = new();
            http.Respond(ShowPageUrl, ShowPageJson);
            http.Respond(PlaylistUrl, EncryptedPlaylist(isProtected: true));
            http.Respond(TokenUrl, EncryptedTokenResponse());
            http.Respond(FinalMediaUrl, MasterPlaylistText);
            MakoCatalog catalog = await CreateCatalogAsync(http, pageCachePersistencePath: persistencePath);

            await catalog.GetSeasonsAsync(PinnedPagePath, CancellationToken.None);
            await catalog.GetStreamLinkAsync(Episode.ToString(), CancellationToken.None);

            string persistedText = File.Exists(persistencePath) ? File.ReadAllText(persistencePath) : string.Empty;
            Assert.DoesNotContain(DecodedTicket, persistedText, StringComparison.Ordinal);
            Assert.DoesNotContain("hmac=ab", persistedText, StringComparison.Ordinal);
            Assert.DoesNotContain("cdn.example.test", persistedText, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(persistencePath);
        }
    }

    /// <summary>
    /// A stale copy is served indefinitely (N2/spec §5.4) — so if that copy ever failed to parse,
    /// a naive <c>GetShowsAsync</c> would throw forever and lock the channel root. It must instead
    /// fall back to the name-only show, the same as when nothing is cached at all.
    /// </summary>
    [Fact]
    public async Task AMalformedCachedShowPageFallsBackToTheNameOnlyShowInsteadOfThrowing()
    {
        FakeMakoHttp http = new();
        (MakoCatalog catalog, MakoPageSource pages) = await BuildStackAsync(http);
        pages.Remember(PinnedPagePath, "<html>not json</html>", TimeSpan.FromHours(12));

        IReadOnlyList<Show> shows = await catalog.GetShowsAsync(CancellationToken.None);

        Show show = Assert.Single(shows);
        Assert.Equal(PinnedPagePath, show.Id);
        Assert.Equal(PinnedShowName, show.Name);
        Assert.Null(show.Overview);
        Assert.Null(show.PosterUrl);
    }

    /// <summary>A malformed episode id must surface as the catalogue's own playback-failure type,
    /// not the parser's raw <see cref="FormatException"/>, so every caller sees one exception
    /// family for "this cannot be played".</summary>
    [Fact]
    public async Task AMalformedEpisodeIdFailsAsAnUnavailableStreamNotARawFormatException()
    {
        FakeMakoHttp http = new();
        MakoCatalog catalog = await CreateCatalogAsync(http);

        StreamUnavailableException exception = await Assert.ThrowsAsync<StreamUnavailableException>(
            () => catalog.GetStreamLinkAsync("not-a-valid-episode-id", CancellationToken.None));

        Assert.Contains("not-a-valid-episode-id", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A minimal show page with <paramref name="seasonCount"/> seasons and a single
    /// episode under whichever season is marked <c>current</c> — the season the page displays.</summary>
    private static string InlineShowPageJson(string showPath, int seasonCount, int displayedSeasonNumber, string episodeTitle)
    {
        string seasonsJson = string.Join(
            ",\n",
            Enumerable.Range(1, seasonCount).Select(number =>
                $$"""{ "seasonTitle": "עונה {{number}}", "current": {{(number == displayedSeasonNumber ? "true" : "false")}}, "pageUrl": "{{showPath}}-s{{number}}" }"""));

        return $$"""
            {
              "pageUrl": "{{showPath}}",
              "seasons": [ {{seasonsJson}} ],
              "menu": [
                {
                  "id": "allEpisodes",
                  "vods": [
                    {
                      "componentLayout": "vod",
                      "title": "{{episodeTitle}}",
                      "itemVcmId": "vcm-{{showPath}}",
                      "channelId": "chan-{{showPath}}",
                      "galleryChannelId": "gal-{{showPath}}",
                      "extraInfo": "פרק 1@01.01.20"
                    }
                  ]
                }
              ]
            }
            """;
    }

    /// <summary>A season's own page (no <c>seasons</c> array of its own) with a single episode —
    /// standing in for a season fetched directly rather than remembered from the show page.</summary>
    private static string InlineSeasonPageJson(string seasonPath, string episodeTitle) =>
        $$"""
        {
          "pageUrl": "{{seasonPath}}",
          "menu": [
            {
              "id": "allEpisodes",
              "vods": [
                {
                  "componentLayout": "vod",
                  "title": "{{episodeTitle}}",
                  "itemVcmId": "vcm-{{seasonPath}}",
                  "channelId": "chan-{{seasonPath}}",
                  "galleryChannelId": "gal-{{seasonPath}}",
                  "extraInfo": "פרק 1@01.01.20"
                }
              ]
            }
          ]
        }
        """;

    private static string EncryptedPlaylist(bool isProtected) =>
        PayloadCipher.Encrypt(
            NewPlaylistKey,
            $$"""
            {
              "media": [ { "url": "{{MediaUrl}}", "cdn": "cdn1", "cdnLB": "50" } ],
              "videoDetails": { "extraParams": { "protectedContent": {{(isProtected ? "true" : "false")}} } }
            }
            """);

    private static string EncryptedTokenResponse() =>
        PayloadCipher.Encrypt(TokenKey, $$"""{ "caseId": 1, "tickets": [ { "ticket": "{{Uri.EscapeDataString(DecodedTicket)}}" } ] }""");

    private static async Task<MakoCatalog> CreateCatalogAsync(
        FakeMakoHttp http, IClock? clock = null, string? pageCachePersistencePath = null, PinnedShow? pinnedShow = null)
    {
        (MakoCatalog catalog, _) = await BuildStackAsync(http, clock, pageCachePersistencePath, pinnedShow).ConfigureAwait(false);
        return catalog;
    }

    /// <summary>Like <see cref="CreateCatalogAsync"/>, but also hands back the
    /// <see cref="MakoPageSource"/> the catalog was built on, for tests that need to plant a
    /// cache entry directly (e.g. a malformed cached page) rather than through a fetch.</summary>
    private static async Task<(MakoCatalog Catalog, MakoPageSource Pages)> BuildStackAsync(
        FakeMakoHttp http, IClock? clock = null, string? pageCachePersistencePath = null, PinnedShow? pinnedShow = null)
    {
        IClock actualClock = clock ?? new FakeClock();
        TimedCache pageCache = new(actualClock, pageCachePersistencePath);
        TimedCache streamLinkCache = new(actualClock);
        MakoConfigLoader config = await WarmedConfigLoaderAsync(http);
        MakoPageSource pages = new(http, config, pageCache, new RecordingLogger<MakoPageSource>());
        StreamResolver resolver = new(http, config, CreateDevice(), () => AppVersion, new RecordingLogger<StreamResolver>());
        MakoCatalog catalog = new(pages, resolver, streamLinkCache, () => pinnedShow ?? Pinned, () => CacheLifetimes.Default);

        return (catalog, pages);
    }

    /// <summary>
    /// Loads the config chain through a throwaway <see cref="FakeMakoHttp"/> into a cache with its
    /// own dedicated, never-advanced clock — deliberately NOT the catalog's own <c>IClock</c>, so a
    /// test advancing that clock by days to pin a page's cache lifetime never incidentally expires
    /// the unrelated 24h config cache too. No config request ever lands on <paramref name="http"/>
    /// — the same "already 24h-fresh" starting point <see cref="MakoPageSourceTests"/> uses.
    /// </summary>
    private static async Task<MakoConfigLoader> WarmedConfigLoaderAsync(IMakoHttp http)
    {
        TimedCache configCache = new(new FakeClock());
        FakeMakoHttp warmUpHttp = new();
        warmUpHttp.Respond(MainConfigUrl, MainConfigJson);
        warmUpHttp.Respond(CoreConfigUrl, CoreConfigJson);
        warmUpHttp.Respond(NotesConfigUrl, NotesConfigJson);
        await new MakoConfigLoader(warmUpHttp, configCache, () => AppVersion).LoadAsync(CancellationToken.None);

        return new MakoConfigLoader(http, configCache, () => AppVersion);
    }

    /// <summary>A missing directory keeps this test free of real disk side effects (see
    /// <c>StreamResolverTests.CreateDevice</c>).</summary>
    private static DeviceIdentity CreateDevice() =>
        new(Path.Combine(Path.GetTempPath(), $"mako-catalog-tests-{Guid.NewGuid():N}-missing-dir", "device.id"));
}
