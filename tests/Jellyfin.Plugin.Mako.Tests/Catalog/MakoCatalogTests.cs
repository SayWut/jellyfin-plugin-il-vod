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

    [Fact]
    public async Task PlayingResolvesOnceAndReusesTheLinkForFiveMinutes()
    {
        string finalUrl = $"{MediaUrl}?{DecodedTicket}";
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
        http.Respond(PlaylistUrl, EncryptedPlaylist(isProtected: false));
        StreamLink resolvedAgain = await catalog.GetStreamLinkAsync(Episode.ToString(), CancellationToken.None);

        Assert.True(http.Requests.Count > requestsAfterFirstResolve);
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

    private static async Task<MakoCatalog> CreateCatalogAsync(FakeMakoHttp http, IClock? clock = null, string? pageCachePersistencePath = null)
    {
        IClock actualClock = clock ?? new FakeClock();
        TimedCache pageCache = new(actualClock, pageCachePersistencePath);
        TimedCache streamLinkCache = new(actualClock);
        MakoConfigLoader config = await WarmedConfigLoaderAsync(http, actualClock);
        MakoPageSource pages = new(http, config, pageCache, new RecordingLogger<MakoPageSource>());
        StreamResolver resolver = new(http, config, CreateDevice(), () => AppVersion, new RecordingLogger<StreamResolver>());

        return new MakoCatalog(pages, resolver, streamLinkCache, actualClock, () => Pinned, () => CacheLifetimes.Default);
    }

    /// <summary>
    /// Loads the config chain through a throwaway <see cref="FakeMakoHttp"/> into a cache shared
    /// with the returned loader, so no config request ever lands on <paramref name="http"/> — the
    /// same "already 24h-fresh" starting point <see cref="MakoPageSourceTests"/> uses.
    /// </summary>
    private static async Task<MakoConfigLoader> WarmedConfigLoaderAsync(IMakoHttp http, IClock clock)
    {
        TimedCache configCache = new(clock);
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
