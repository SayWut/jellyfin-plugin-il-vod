using Jellyfin.Plugin.Mako.Browsing;
using Jellyfin.Plugin.Mako.Cache;
using Jellyfin.Plugin.Mako.Catalog;
using Jellyfin.Plugin.Mako.MakoApi;
using Jellyfin.Plugin.Mako.Model;
using Jellyfin.Plugin.Mako.Tests.Support;
using MediaBrowser.Controller.Channels;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.Mako.Tests.Catalog;

public class MakoCatalogTests
{
    private const string AppVersion = "43.0";
    private const string ShemeshPagePath = "/mako-vod-keshet/shemesh";
    private const string ArabicWorkPagePath = "/mako-vod-keshet/Arabic-work";
    private const string SeasonOnePagePath = "/mako-vod-keshet/shemesh-s1";

    // The real show-page-shemesh.json fixture's own pageUrl — the season it displays — which is a
    // DIFFERENT path from ShemeshPagePath (the show's own landing path). Fetching it directly
    // under this path is the one way to reproduce "a show's path IS its displayed season's path"
    // with the real fixture, since ShemeshPagePath and DisplayedSeasonPath no longer coincide now
    // that GetShowsAsync no longer keys shows by their season.
    private const string DisplayedSeasonPath = "/mako-vod-keshet/shemesh-s6";

    // The three real genres genre-menu.json names besides דרמה/קומדיה: SeededHttp() gives each one
    // throwaway show at its own path, rather than an empty list, per §6.2's "mako's real genres
    // are never empty" ruling.
    private const string RealityPlaceholderPath = "/mako-vod-keshet/placeholder-1"; // ריאליטי
    private const string DocumentaryPlaceholderPath = "/mako-vod-keshet/placeholder-2"; // דוקומנטרי
    private const string CookingPlaceholderPath = "/mako-vod-keshet/placeholder-3"; // בישול

    private const string NewPlaylistKey = "TESTKEY-PLAYLIST-NEW-24b";
    private const string TokenKey = "TESTKEY-TOKEN-TICKET-24b";
    private const string MediaUrl = "https://cdn.example.test/master.m3u8";
    private const string DecodedTicket = "hdnea=st=1~exp=2~acl=/*~hmac=ab";

    /// <summary>What mako serves when it refuses a page without admitting to it: HTML, status 200.</summary>
    private const string HtmlErrorPage = "<html><body>Access Denied</body></html>";

    private static readonly string MainConfigJson = FixtureFile.ReadText("main-config.json");
    private static readonly string CoreConfigJson = FixtureFile.ReadText("core-config.json");
    private static readonly string NotesConfigJson = FixtureFile.ReadText("notes-config.json");
    private static readonly string ShowPageJson = FixtureFile.ReadText("show-page-shemesh.json");
    private static readonly string SeasonOnePageJson = FixtureFile.ReadText("season-page-shemesh-s1.json");
    private static readonly string MasterPlaylistText = FixtureFile.ReadText("master.m3u8");
    private static readonly string MenuJson = FixtureFile.ReadText("genre-menu.json");
    private static readonly string ComedyJson = FixtureFile.ReadText("genre-list-comedy.json");
    private static readonly string DramaJson = FixtureFile.ReadText("genre-list-drama.json");
    private static readonly string EmptyListJson = """{"items":[]}""";

    private static readonly string MainConfigUrl = MakoUrls.MainConfigUrl(AppVersion);
    private static readonly string CoreConfigUrl = MakoConfig.CoreConfigUrlOf(MainConfigJson);
    private static readonly string NotesConfigUrl = MakoConfig.NotesUrlOf(CoreConfigJson);
    private static readonly MakoConfig ParsedConfig = MakoConfig.Parse(MainConfigJson, CoreConfigJson, NotesConfigJson);
    private static readonly string BaseUrlTemplate = ParsedConfig.BaseUrlTemplate;

    private static readonly string ShowPageUrl = MakoUrls.Page(BaseUrlTemplate, ShemeshPagePath);
    private static readonly string SeasonOneUrl = MakoUrls.Page(BaseUrlTemplate, SeasonOnePagePath);
    private static readonly string DisplayedSeasonUrl = MakoUrls.Page(BaseUrlTemplate, DisplayedSeasonPath);

    private static readonly EpisodeKey Episode = new("vcm1", "channel1", "gallery1");
    private static readonly string PlaylistUrl =
        MakoUrls.Playlist(ParsedConfig.PlaylistUrlTemplate, Episode.VcmId, Episode.ChannelId, Episode.GalleryChannelId);
    private static readonly string TokenUrl = ParsedConfig.TokenUrl;
    private static readonly string FinalMediaUrl = $"{MediaUrl}?{DecodedTicket}";

    [Fact]
    public async Task TheChannelListsEveryShowOfEveryRealGenreOnceWithItsGenres()
    {
        FakeMakoHttp http = SeededHttp();
        MakoCatalog catalog = await CreateCatalogAsync(http);

        IReadOnlyList<Show> shows = await catalog.GetShowsAsync(CancellationToken.None);

        Assert.Equal(11, shows.Count); // 1 reality + 1 documentary + 5 comedy + 4 drama − 1 shared + 1 cooking
        Show shemesh = Assert.Single(shows, show => show.Id == ShemeshPagePath);
        // Assert.Equivalent, not Assert.Equal: Show's Genres is an IReadOnlyList<string>, whose
        // default equality (used by the compiler-generated record Equals) is reference equality —
        // two independently-built lists holding the same single genre would never compare equal.
        Assert.Equivalent(
            new Show(ShemeshPagePath, "שמש", "https://img.mako.co.il/2025/06/09/Shemesh_e.jpg", Overview: null, Genres: ["קומדיה"]),
            shemesh,
            strict: true);
        Show shared = Assert.Single(shows, show => show.Id == ArabicWorkPagePath);
        Assert.Equal(new[] { "דרמה", "קומדיה" }, shared.Genres); // menu order: drama comes before comedy

        // Order-sensitive: shows are returned in first-appearance order, not grouped by genre or
        // sorted by name. Menu order is ריאליטי, דוקומנטרי, דרמה, קומדיה, בישול, so the reality and
        // documentary placeholders lead; דרמה then contributes its four shows, in genre-list-drama
        // .json's own order; קומדיה contributes only the four shows not already seen (עבודה ערבית/
        // Arabic-work is skipped a second time), in genre-list-comedy.json's own order; the cooking
        // placeholder trails.
        Assert.Equal(
            new[]
            {
                RealityPlaceholderPath,
                DocumentaryPlaceholderPath,
                ArabicWorkPagePath,
                "/mako-vod-keshet/dani_hollywood",
                "/mako-vod-keshet/lowest_place_world",
                "/mako-vod-keshet/nutuk_drama",
                ShemeshPagePath,
                "/mako-vod-keshet/kobi_maimon_standup",
                "/mako-vod-keshet/life_is_difficult",
                "/mako-vod-keshet/matan_peretz_standup",
                CookingPlaceholderPath,
            },
            shows.Select(show => show.Id));
    }

    /// <summary>A show can legitimately carry more than one real genre (<c>ArabicWorkPagePath</c>
    /// above), but a single list naming the very same show twice must not double that list's own
    /// genre onto it.</summary>
    [Fact]
    public async Task AShowListedTwiceInOneGenreCarriesThatGenreOnce()
    {
        const string genreName = "ריאליטי";
        const string duplicateShowPath = "/mako-vod-keshet/duplicate-in-one-genre";
        string listWithADuplicateEntry = $$"""
            {
              "items": [
                { "itemVcmId": "dup1", "title": "כפול", "pageUrl": "{{duplicateShowPath}}", "pic": null },
                { "itemVcmId": "dup2", "title": "כפול", "pageUrl": "{{duplicateShowPath}}", "pic": null }
              ]
            }
            """;
        FakeMakoHttp http = SeededHttp();
        Genre realityGenre = MakoIndex.ParseGenres(MenuJson).Single(genre => genre.Name == genreName);
        http.Respond(MakoUrls.Page(BaseUrlTemplate, realityGenre.PagePath), listWithADuplicateEntry);
        MakoCatalog catalog = await CreateCatalogAsync(http);

        IReadOnlyList<Show> shows = await catalog.GetShowsAsync(CancellationToken.None);

        Show duplicate = Assert.Single(shows, show => show.Id == duplicateShowPath);
        Assert.Equal(new[] { genreName }, duplicate.Genres);
    }

    [Fact]
    public async Task OpeningTheChannelCostsTheMenuAndOneRequestPerRealGenreAndThenNothing()
    {
        FakeMakoHttp http = SeededHttp();
        MakoCatalog catalog = await CreateCatalogAsync(http);

        await catalog.GetShowsAsync(CancellationToken.None);
        Assert.Equal(6, http.Requests.Count(url => url.Contains(MakoUrls.BrowseHost, StringComparison.Ordinal)));
        Assert.DoesNotContain(http.Requests, url => url.Contains("filter=provider", StringComparison.Ordinal) || url.Contains("ABC_Programs", StringComparison.Ordinal) || url.Contains("filter=playlists", StringComparison.Ordinal));

        await catalog.GetShowsAsync(CancellationToken.None);
        Assert.Equal(6, http.Requests.Count(url => url.Contains(MakoUrls.BrowseHost, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AGenreListThatFailsFailsTheWholeAnswerRatherThanShorteningIt()
    {
        FakeMakoHttp http = SeededHttp();
        http.Challenge(MakoUrls.Page(BaseUrlTemplate, MakoIndex.ParseGenres(MenuJson)[2].PagePath)); // דרמה
        MakoCatalog catalog = await CreateCatalogAsync(http);

        await Assert.ThrowsAsync<BotChallengeException>(() => catalog.GetShowsAsync(CancellationToken.None));
    }

    /// <summary>§6.2's controller ruling: mako's real genres are never empty, so a genre list that
    /// parses but names no show is a failure too, not a shorter answer — the same destructive hole
    /// as <see cref="AMenuThatNamesNoGenreFailsTheChannelAndIsNeverCached"/> below, one level
    /// deeper. Only ריאליטי's list is broken; the other four genres still answer normally.</summary>
    [Fact]
    public async Task AGenreListThatNamesNoShowFailsTheWholeAnswerAndIsNeverCached()
    {
        FakeMakoHttp http = SeededHttp();
        string realityListUrl = MakoUrls.Page(BaseUrlTemplate, MakoIndex.ParseGenres(MenuJson)[0].PagePath); // ריאליטי
        http.Respond(realityListUrl, EmptyListJson);
        MakoCatalog catalog = await CreateCatalogAsync(http);

        await Assert.ThrowsAsync<FormatException>(() => catalog.GetShowsAsync(CancellationToken.None));
        await Assert.ThrowsAsync<FormatException>(() => catalog.GetShowsAsync(CancellationToken.None));
        Assert.Equal(2, http.Requests.Count(url => url == realityListUrl));
    }

    /// <summary>Clone of <see cref="AMenuThatIsNotJsonIsNeverCachedAndFailsTheChannel"/> one level
    /// deeper: a genre's own list answering HTML must not be cached either.</summary>
    [Fact]
    public async Task AGenreListThatIsNotJsonIsNeverCachedAndFailsTheChannel()
    {
        FakeMakoHttp http = SeededHttp();
        string dramaListUrl = MakoUrls.Page(BaseUrlTemplate, MakoIndex.ParseGenres(MenuJson)[2].PagePath); // דרמה
        http.Respond(dramaListUrl, HtmlErrorPage);
        MakoCatalog catalog = await CreateCatalogAsync(http);

        await Assert.ThrowsAsync<FormatException>(() => catalog.GetShowsAsync(CancellationToken.None));
        await Assert.ThrowsAsync<FormatException>(() => catalog.GetShowsAsync(CancellationToken.None));
        Assert.Equal(2, http.Requests.Count(url => url == dramaListUrl));
    }

    [Fact]
    public async Task AStaleCatalogueIsServedWhenMakoRefusesTheRefetch()
    {
        FakeClock clock = new();
        FakeMakoHttp http = SeededHttp();
        MakoCatalog catalog = await CreateCatalogAsync(http, clock); // builds both TimedCaches over the given FakeClock
        IReadOnlyList<Show> first = await catalog.GetShowsAsync(CancellationToken.None);

        clock.Advance(CatalogueLifetimes.Default.Catalogue + TimeSpan.FromMinutes(1));
        foreach (Genre genre in MakoIndex.ParseGenres(MenuJson)) { http.Challenge(MakoUrls.Page(BaseUrlTemplate, genre.PagePath)); }
        http.Challenge(MakoUrls.Page(BaseUrlTemplate, MakoIndexPaths.GenreMenu));

        // Assert.Equivalent, not Assert.Equal: see the same note on TheChannelListsEveryShowOf... —
        // Show's Genres list breaks record/list reference equality for any non-empty genre list.
        Assert.Equivalent(first, await catalog.GetShowsAsync(CancellationToken.None), strict: true);
    }

    [Fact]
    public async Task AMenuThatIsNotJsonIsNeverCachedAndFailsTheChannel()
    {
        FakeMakoHttp http = SeededHttp();
        http.Respond(MakoUrls.Page(BaseUrlTemplate, MakoIndexPaths.GenreMenu), HtmlErrorPage);
        MakoCatalog catalog = await CreateCatalogAsync(http);

        await Assert.ThrowsAsync<FormatException>(() => catalog.GetShowsAsync(CancellationToken.None));
        await Assert.ThrowsAsync<FormatException>(() => catalog.GetShowsAsync(CancellationToken.None));
        Assert.Equal(2, http.Requests.Count(url => url.Contains("filter=menu", StringComparison.Ordinal)));
    }

    /// <summary>Critical, §6.2's controller ruling: mako's real genres are never empty, so a menu
    /// that parses but names none is treated as a failure too — the same rule as an unparseable
    /// menu just above, closing the hole where an empty/shorter root answer would make Jellyfin
    /// delete every show it already held for 3 hours, then cache the useless empty answer for 24.</summary>
    [Fact]
    public async Task AMenuThatNamesNoGenreFailsTheChannelAndIsNeverCached()
    {
        FakeMakoHttp http = SeededHttp();
        http.Respond(MakoUrls.Page(BaseUrlTemplate, MakoIndexPaths.GenreMenu), """{"menuItems":[]}""");
        MakoCatalog catalog = await CreateCatalogAsync(http);

        await Assert.ThrowsAsync<FormatException>(() => catalog.GetShowsAsync(CancellationToken.None));
        await Assert.ThrowsAsync<FormatException>(() => catalog.GetShowsAsync(CancellationToken.None));
        Assert.Equal(2, http.Requests.Count(url => url.Contains("filter=menu", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task EpisodesAreNamedAfterTheirShowFromTheCachedListsWithoutARequest()
    {
        FakeMakoHttp http = SeededHttp();
        MakoCatalog catalog = await CreateCatalogAsync(http);
        await catalog.GetShowsAsync(CancellationToken.None);
        int requestsBefore = http.Requests.Count;

        IReadOnlyList<Episode> episodes = await catalog.GetEpisodesAsync(ShemeshPagePath, SeasonOnePagePath, CancellationToken.None);

        Assert.All(episodes, episode => Assert.Equal("שמש", episode.ShowName));
        Assert.Equal(requestsBefore + 1, http.Requests.Count); // the season page only
    }

    [Fact]
    public async Task AnUnknownShowStillListsItsEpisodesWithNoSeriesName()
    {
        FakeMakoHttp http = SeededHttp();
        MakoCatalog catalog = await CreateCatalogAsync(http);

        IReadOnlyList<Episode> episodes = await catalog.GetEpisodesAsync("/mako-vod-keshet/never-listed", SeasonOnePagePath, CancellationToken.None);

        Assert.NotEmpty(episodes);
        Assert.All(episodes, episode => Assert.Equal(string.Empty, episode.ShowName));
        Assert.DoesNotContain(http.Requests, url => url.Contains("mako-vod-index", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnyShowsSeasonsCanBeOpenedNotJustAPinnedOne()
    {
        FakeMakoHttp http = SeededHttp();
        MakoCatalog catalog = await CreateCatalogAsync(http);

        Assert.NotEmpty(await catalog.GetSeasonsAsync(ShemeshPagePath, CancellationToken.None));
    }

    /// <summary>
    /// The show path and the season it displays are DIFFERENT cache keys here (unlike
    /// <see cref="TheShowPageIsNotDowngradedToTheCurrentSeasonLifetimeWhenItsOwnPathIsTheDisplayedSeason"/>
    /// below, where they deliberately coincide) — so this is a genuine test of <c>Remember</c>
    /// writing under a distinct key, not a same-key overwrite that would pass even if nothing were
    /// remembered at all.
    /// </summary>
    [Fact]
    public async Task TheDisplayedSeasonIsRememberedUnderItsOwnPathSoOpeningItCostsNothing()
    {
        const string showPath = "/x/show-latest";
        const string latestSeasonPath = "/x/show-latest-s3";
        string showJson = InlineShowPageJson(showPath, seasonCount: 3, displayedSeasonNumber: 3, episodeTitle: "S3 Episode");
        FakeMakoHttp http = new();
        http.Respond(MakoUrls.Page(BaseUrlTemplate, showPath), showJson);
        MakoCatalog catalog = await CreateCatalogAsync(http);

        await catalog.GetSeasonsAsync(showPath, CancellationToken.None);
        int requestsAfterSeasons = http.Requests.Count;

        IReadOnlyList<Episode> episodes = await catalog.GetEpisodesAsync(showPath, latestSeasonPath, CancellationToken.None);

        Assert.Equal(requestsAfterSeasons, http.Requests.Count);
        Assert.Equal("S3 Episode", Assert.Single(episodes).Name);
    }

    /// <summary>
    /// The wrong-season guard: when a show's page displays an EARLIER season than the latest,
    /// opening that displayed season is still free (remembered), but opening the actual latest
    /// season costs a request and returns THAT season's own episodes — proving the two are never
    /// confused with one another.
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
        http.Respond(MakoUrls.Page(BaseUrlTemplate, showPath), showJson);
        http.Respond(MakoUrls.Page(BaseUrlTemplate, latestSeasonPath), latestSeasonJson);
        MakoCatalog catalog = await CreateCatalogAsync(http);

        await catalog.GetSeasonsAsync(showPath, CancellationToken.None);
        int requestsAfterSeasons = http.Requests.Count;

        IReadOnlyList<Episode> displayedEpisodes = await catalog.GetEpisodesAsync(showPath, displayedSeasonPath, CancellationToken.None);
        Assert.Equal(requestsAfterSeasons, http.Requests.Count);
        Assert.Equal("S1 Episode", Assert.Single(displayedEpisodes).Name);

        IReadOnlyList<Episode> latestEpisodes = await catalog.GetEpisodesAsync(showPath, latestSeasonPath, CancellationToken.None);
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

        await catalog.GetEpisodesAsync(ShemeshPagePath, SeasonOnePagePath, CancellationToken.None);
        int requestsAfterFirst = http.Requests.Count;

        clock.Advance(TimeSpan.FromHours(6) + TimeSpan.FromMinutes(1));
        await catalog.GetEpisodesAsync(ShemeshPagePath, SeasonOnePagePath, CancellationToken.None);
        Assert.Equal(requestsAfterFirst, http.Requests.Count);

        clock.Advance(TimeSpan.FromDays(7));
        await catalog.GetEpisodesAsync(ShemeshPagePath, SeasonOnePagePath, CancellationToken.None);
        Assert.Equal(requestsAfterFirst + 1, http.Requests.Count);
    }

    /// <summary>
    /// The real fixture's own displayed season IS its own highest-numbered season — both
    /// <see cref="DisplayedSeasonPath"/> — the exact case where a naive, unconditional
    /// <c>Remember</c> would overwrite the show page's own 12h entry with the season's 6h one,
    /// silently downgrading it. The guard must skip the remember when the two ids already
    /// coincide, so the show page keeps its full 12h lifetime. Fetched directly under
    /// <see cref="DisplayedSeasonPath"/> (not <see cref="ShemeshPagePath"/>, which is now a
    /// distinct cache key from its own displayed season) so the coincidence is genuine.
    /// </summary>
    [Fact]
    public async Task TheShowPageIsNotDowngradedToTheCurrentSeasonLifetimeWhenItsOwnPathIsTheDisplayedSeason()
    {
        FakeClock clock = new();
        FakeMakoHttp http = new();
        http.Respond(DisplayedSeasonUrl, ShowPageJson);
        MakoCatalog catalog = await CreateCatalogAsync(http, clock);

        await catalog.GetSeasonsAsync(DisplayedSeasonPath, CancellationToken.None);
        int requestsAfterFirst = http.Requests.Count;

        // Past the 6h "current season" lifetime, but still within the show page's own 12h one.
        clock.Advance(TimeSpan.FromHours(6) + TimeSpan.FromMinutes(1));
        await catalog.GetSeasonsAsync(DisplayedSeasonPath, CancellationToken.None);

        Assert.Equal(requestsAfterFirst, http.Requests.Count);
    }

    [Fact]
    public async Task TheLatestSeasonPageIsRefetchedAfterSixHoursWhenOpenedDirectly()
    {
        FakeClock clock = new();
        FakeMakoHttp http = new();
        http.Respond(DisplayedSeasonUrl, ShowPageJson);
        MakoCatalog catalog = await CreateCatalogAsync(http, clock);

        // Opened directly through GetEpisodesAsync, never through GetSeasonsAsync, so this is a
        // fresh cache entry governed purely by the "is this the latest season" 6h lifetime.
        await catalog.GetEpisodesAsync(ShemeshPagePath, DisplayedSeasonPath, CancellationToken.None);
        int requestsAfterFirst = http.Requests.Count;

        clock.Advance(TimeSpan.FromHours(6));
        await catalog.GetEpisodesAsync(ShemeshPagePath, DisplayedSeasonPath, CancellationToken.None);

        Assert.Equal(requestsAfterFirst + 1, http.Requests.Count);
    }

    [Fact]
    public async Task TheShowPageSurvivesTwelveHoursIndependentlyOfTheRememberedLatestSeason()
    {
        const string showPath = "/x/show-lifetime";
        string showJson = InlineShowPageJson(showPath, seasonCount: 3, displayedSeasonNumber: 3, episodeTitle: "S3 Episode");
        FakeClock clock = new();
        FakeMakoHttp http = new();
        http.Respond(MakoUrls.Page(BaseUrlTemplate, showPath), showJson);
        MakoCatalog catalog = await CreateCatalogAsync(http, clock);

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

        // A fresh resolve costs exactly the playlist GET, the master-playlist GET and the
        // variant-playlist GET that measures the duration (config is already cached) — never a
        // loose "some new requests happened".
        Assert.Equal(requestsAfterFirstResolve + 3, http.Requests.Count);
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

            await catalog.GetSeasonsAsync(ShemeshPagePath, CancellationToken.None);
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
    /// mako's page endpoint answering an HTML error page with status 200 must not poison the
    /// persisted cache for the next 12 hours: the page is rejected before it is written, so the
    /// folder fails now and the next open asks mako again rather than replaying the same rubbish
    /// from disk. The channel is in the picture because failing — never answering empty, which
    /// Jellyfin would cache and act on — is its half of that promise.
    /// </summary>
    [Fact]
    public async Task AnHtmlShowPageIsNeverCachedAndFailsTheFolder()
    {
        FakeMakoHttp http = new();
        http.Respond(ShowPageUrl, HtmlErrorPage);
        MakoChannel channel = ChannelOver(await CreateCatalogAsync(http));

        await Assert.ThrowsAsync<FormatException>(() => OpenFolder(channel, FolderId.ForShow(ShemeshPagePath)));
        Assert.Single(http.Requests);

        await Assert.ThrowsAsync<FormatException>(() => OpenFolder(channel, FolderId.ForShow(ShemeshPagePath)));
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public async Task AnHtmlSeasonPageIsNeverCachedAndFailsTheFolder()
    {
        FakeMakoHttp http = new();
        http.Respond(SeasonOneUrl, HtmlErrorPage);
        MakoChannel channel = ChannelOver(await CreateCatalogAsync(http));

        await Assert.ThrowsAsync<FormatException>(() => OpenFolder(channel, FolderId.ForSeason(ShemeshPagePath, SeasonOnePagePath)));
        Assert.Single(http.Requests);

        await Assert.ThrowsAsync<FormatException>(() => OpenFolder(channel, FolderId.ForSeason(ShemeshPagePath, SeasonOnePagePath)));
        Assert.Equal(2, http.Requests.Count);
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

    /// <summary>The genre menu plus the five real genre lists it names, matching the fixtures'
    /// own genre order (ריאליטי, דוקומנטרי, דרמה, קומדיה, בישול): comedy and drama carry the real
    /// show lists (including the show shared between them); the other three real genres answer
    /// one throwaway placeholder show each rather than an empty list — mako's real genres are
    /// never empty (spec §6.2), so a genre list this suite seeds must not be either. The show page
    /// and season-one page fixtures stay registered under <see cref="ShemeshPagePath"/>'s and
    /// <see cref="SeasonOnePagePath"/>'s own URLs, for the tests that open them.</summary>
    private static FakeMakoHttp SeededHttp()
    {
        FakeMakoHttp http = new();
        http.Respond(MakoUrls.Page(BaseUrlTemplate, MakoIndexPaths.GenreMenu), MenuJson);
        foreach (Genre genre in MakoIndex.ParseGenres(MenuJson))
        {
            string listJson = genre.Name switch
            {
                "קומדיה" => ComedyJson,
                "דרמה" => DramaJson,
                "ריאליטי" => PlaceholderListJson(genre.Name, RealityPlaceholderPath),
                "דוקומנטרי" => PlaceholderListJson(genre.Name, DocumentaryPlaceholderPath),
                "בישול" => PlaceholderListJson(genre.Name, CookingPlaceholderPath),
                _ => EmptyListJson,
            };
            http.Respond(MakoUrls.Page(BaseUrlTemplate, genre.PagePath), listJson);
        }

        http.Respond(ShowPageUrl, ShowPageJson);
        http.Respond(SeasonOneUrl, SeasonOnePageJson);
        return http;
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

    /// <summary>One throwaway show for a genre this suite does not otherwise carry real fixtures
    /// for — never <see cref="EmptyListJson"/>, since §6.2 treats a real genre naming no show as a
    /// failure, not an empty answer.</summary>
    private static string PlaceholderListJson(string genreName, string pagePath) =>
        $$"""{"items":[{"itemVcmId":"x","title":"{{genreName}} placeholder","pageUrl":"{{pagePath}}","pic":null}]}""";

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

    /// <summary>The real <see cref="MakoChannel"/> over a real catalogue, for the tests whose
    /// subject is what a user sees when a page cannot be parsed.</summary>
    private static MakoChannel ChannelOver(ICatalog catalog) =>
        new(new Lazy<ICatalog>(() => catalog), new RecordingLogger<MakoChannel>());

    private static Task<ChannelItemResult> OpenFolder(MakoChannel channel, FolderId folder) =>
        channel.GetChannelItems(new InternalChannelItemQuery { FolderId = folder.ToString(), UserId = Guid.NewGuid() }, CancellationToken.None);

    private static Task<MakoCatalog> CreateCatalogAsync(FakeMakoHttp http, string? pageCachePersistencePath = null) =>
        CreateCatalogAsync(http, new FakeClock(), pageCachePersistencePath);

    /// <summary>Builds a catalog whose two <see cref="TimedCache"/>s (page cache and stream-link
    /// cache) both run over <paramref name="clock"/>, so a test can advance one clock and see both
    /// caches age together — needed by <see cref="AStaleCatalogueIsServedWhenMakoRefusesTheRefetch"/>.</summary>
    private static async Task<MakoCatalog> CreateCatalogAsync(FakeMakoHttp http, IClock clock, string? pageCachePersistencePath = null)
    {
        TimedCache pageCache = new(clock, pageCachePersistencePath);
        TimedCache streamLinkCache = new(clock);
        MakoConfigLoader config = await WarmedConfigLoaderAsync(http).ConfigureAwait(false);
        MakoPageSource pages = new(http, config, pageCache, new RecordingLogger<MakoPageSource>());
        StreamResolver resolver = new(http, config, CreateDevice(), () => AppVersion, new RecordingLogger<StreamResolver>());

        return new MakoCatalog(pages, resolver, streamLinkCache, () => CatalogueLifetimes.Default, NullLogger<MakoCatalog>.Instance);
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
