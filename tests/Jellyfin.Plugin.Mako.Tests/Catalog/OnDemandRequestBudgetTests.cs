using Jellyfin.Plugin.Mako.Cache;
using Jellyfin.Plugin.Mako.Catalog;
using Jellyfin.Plugin.Mako.MakoApi;
using Jellyfin.Plugin.Mako.Model;
using Jellyfin.Plugin.Mako.Tests.Support;

namespace Jellyfin.Plugin.Mako.Tests.Catalog;

/// <summary>
/// N1's definition of done (spec §2): one <see cref="FakeMakoHttp"/> wired through the real
/// <see cref="MakoConfigLoader"/>, <see cref="MakoPageSource"/>, <see cref="StreamResolver"/> and
/// <see cref="MakoCatalog"/>, counting requests after every step of a user session — browsing the
/// pinned show, its current season, a past season, and pressing play — to prove that only what
/// each action actually needs is ever asked of mako, nothing ahead of it and nothing repeated
/// inside a cache's lifetime.
/// </summary>
public class OnDemandRequestBudgetTests
{
    private const string AppVersion = "43.0";

    // The real default: the pinned page is the show's own landing path, which is NOT the same as
    // whichever season page it happens to display (that is DisplayedSeasonPath below). Serving
    // the show-page fixture's JSON at this distinct url is what makes the "current season" row of
    // the table below a genuine test of MakoCatalog.Remember-ing under a different cache key,
    // rather than a same-key overwrite that would pass even if nothing were remembered at all.
    private const string PinnedPagePath = "/mako-vod-keshet/shemesh";
    private const string DisplayedSeasonPath = "/mako-vod-keshet/shemesh-s6";
    private const string PinnedShowName = "שמש";
    private const string SeasonOnePagePath = "/mako-vod-keshet/shemesh-s1";
    private const string NewPlaylistKey = "TESTKEY-PLAYLIST-NEW-24b";
    private const string TokenKey = "TESTKEY-TOKEN-TICKET-24b";
    private const string MediaUrl = "https://cdn.example.test/master.m3u8";
    private const string DecodedTicket = "hdnea=st=1~exp=2~acl=/*~hmac=ab";

    // The season-1 fixture's own first episode, so "the protected episode" is one that actually
    // belongs to the season the test opens rather than an id invented for the occasion.
    private const string ProtectedEpisodeVcmId = "d13f539206c35910VgnVCM200000650a10acRCRD";
    private const string ProtectedEpisodeChannelId = "4e091bfd191d0810VgnVCM200000650a10acRCRD";
    private const string ProtectedEpisodeGalleryChannelId = "d13f539206c35910VgnVCM200000650a10acRCRD";

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

    private static readonly string PinnedPageUrl = MakoUrls.Page(ParsedConfig.BaseUrlTemplate, PinnedPagePath);
    private static readonly string SeasonOneUrl = MakoUrls.Page(ParsedConfig.BaseUrlTemplate, SeasonOnePagePath);

    private static readonly EpisodeKey ProtectedEpisode =
        new(ProtectedEpisodeVcmId, ProtectedEpisodeChannelId, ProtectedEpisodeGalleryChannelId);
    private static readonly string PlaylistUrl = MakoUrls.Playlist(
        ParsedConfig.PlaylistUrlTemplate, ProtectedEpisode.VcmId, ProtectedEpisode.ChannelId, ProtectedEpisode.GalleryChannelId);
    private static readonly string TokenUrl = ParsedConfig.TokenUrl;
    private static readonly string FinalMediaUrl = $"{MediaUrl}?{DecodedTicket}";

    [Fact]
    public async Task TheRequestBudgetMatchesEveryStepOfTheOnDemandTable()
    {
        FakeMakoHttp http = SeededHttp();
        MakoCatalog catalog = await CreateCatalogAsync(http);

        // | construct everything; GetShowsAsync | 0 |
        await catalog.GetShowsAsync(CancellationToken.None);
        Assert.Equal(0, BrowseHostRequestCount(http));

        // | GetSeasonsAsync(show) | 1 (the show page, fetched under the PINNED path) |
        await catalog.GetSeasonsAsync(PinnedPagePath, CancellationToken.None);
        Assert.Equal(1, BrowseHostRequestCount(http));

        // | GetEpisodesAsync(current season) | 1 (served from the show page, remembered under the
        // DISPLAYED season's own — different — path) |
        await catalog.GetEpisodesAsync(DisplayedSeasonPath, CancellationToken.None);
        Assert.Equal(1, BrowseHostRequestCount(http));

        // | GetEpisodesAsync(season 1) | 2 |
        await catalog.GetEpisodesAsync(SeasonOnePagePath, CancellationToken.None);
        Assert.Equal(2, BrowseHostRequestCount(http));

        // | GetEpisodesAsync(season 1) again | 2 |
        await catalog.GetEpisodesAsync(SeasonOnePagePath, CancellationToken.None);
        Assert.Equal(2, BrowseHostRequestCount(http));

        // | GetSeasonsAsync(show) again | 2 |
        await catalog.GetSeasonsAsync(PinnedPagePath, CancellationToken.None);
        Assert.Equal(2, BrowseHostRequestCount(http));

        // | GetStreamLinkAsync(protected episode) | 3 on www (playlist) + 1 POST to mass.mako.co.il + 1 GET to the CDN |
        StreamLink link = await catalog.GetStreamLinkAsync(ProtectedEpisode.ToString(), CancellationToken.None);
        Assert.Equal(3, BrowseHostRequestCount(http));
        Assert.Equal(1, RequestCountTo(http, TokenUrl));
        Assert.Equal(1, RequestCountTo(http, FinalMediaUrl));
        Assert.Equal(FinalMediaUrl, link.Url);

        // The token endpoint is POSTed to; every other request (pages, playlist, the CDN) is a GET.
        Assert.Contains(http.Calls, call => call.Method == "POST" && call.Url == TokenUrl);
        Assert.All(http.Calls.Where(call => call.Url != TokenUrl), call => Assert.Equal("GET", call.Method));

        // | same episode again within 5 min | unchanged |
        StreamLink again = await catalog.GetStreamLinkAsync(ProtectedEpisode.ToString(), CancellationToken.None);
        Assert.Equal(3, BrowseHostRequestCount(http));
        Assert.Equal(1, RequestCountTo(http, TokenUrl));
        Assert.Equal(1, RequestCountTo(http, FinalMediaUrl));
        Assert.Equal(link, again);
    }

    [Fact]
    public async Task NothingIsEverRequestedForSeasonsNobodyOpened()
    {
        FakeMakoHttp http = SeededHttp();
        MakoCatalog catalog = await CreateCatalogAsync(http);

        await catalog.GetSeasonsAsync(PinnedPagePath, CancellationToken.None);
        await catalog.GetEpisodesAsync(SeasonOnePagePath, CancellationToken.None);

        foreach (string untouchedSeasonSlug in new[] { "shemesh-s2", "shemesh-s3", "shemesh-s4", "shemesh-s5" })
        {
            Assert.DoesNotContain(http.Requests, url => url.Contains(untouchedSeasonSlug, StringComparison.Ordinal));
        }

        // Positive control: the same matcher style DOES find the season that was actually opened,
        // so the assertions above are proven to be checking something real rather than passing
        // vacuously because the matcher itself never matches anything.
        Assert.Contains(http.Requests, url => url.Contains("shemesh-s1", StringComparison.Ordinal));
    }

    private static FakeMakoHttp SeededHttp()
    {
        FakeMakoHttp http = new();
        http.Respond(PinnedPageUrl, ShowPageJson);
        http.Respond(SeasonOneUrl, SeasonOnePageJson);
        http.Respond(PlaylistUrl, EncryptedProtectedPlaylist());
        http.Respond(TokenUrl, EncryptedTokenResponse());
        http.Respond(FinalMediaUrl, MasterPlaylistText);
        return http;
    }

    private static int BrowseHostRequestCount(FakeMakoHttp http) => RequestCountToHost(http, MakoUrls.BrowseHost);

    private static int RequestCountToHost(FakeMakoHttp http, string host) =>
        http.Requests.Count(url => new Uri(url).Host == host);

    private static int RequestCountTo(FakeMakoHttp http, string url) => http.Requests.Count(requested => requested == url);

    private static string EncryptedProtectedPlaylist() =>
        PayloadCipher.Encrypt(
            NewPlaylistKey,
            $$"""
            {
              "media": [ { "url": "{{MediaUrl}}", "cdn": "cdn1", "cdnLB": "50" } ],
              "videoDetails": { "extraParams": { "protectedContent": true } }
            }
            """);

    private static string EncryptedTokenResponse() =>
        PayloadCipher.Encrypt(TokenKey, $$"""{ "caseId": 1, "tickets": [ { "ticket": "{{Uri.EscapeDataString(DecodedTicket)}}" } ] }""");

    private static async Task<MakoCatalog> CreateCatalogAsync(FakeMakoHttp http)
    {
        FakeClock clock = new();
        TimedCache pageCache = new(clock);
        TimedCache streamLinkCache = new(clock);
        MakoConfigLoader config = await WarmedConfigLoaderAsync(http);
        MakoPageSource pages = new(http, config, pageCache, new RecordingLogger<MakoPageSource>());
        StreamResolver resolver = new(http, config, CreateDevice(), () => AppVersion, new RecordingLogger<StreamResolver>());

        return new MakoCatalog(pages, resolver, streamLinkCache, () => Pinned, () => CacheLifetimes.Default);
    }

    /// <summary>
    /// Loads the config chain through a throwaway <see cref="FakeMakoHttp"/> into a cache with its
    /// own dedicated clock, so the config chain — which itself touches
    /// <see cref="MakoUrls.BrowseHost"/> for the notes document — never counts against the budget
    /// this test pins, matching a plugin whose config is already 24h-fresh by the time a user
    /// opens a folder.
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
        new(Path.Combine(Path.GetTempPath(), $"mako-budget-tests-{Guid.NewGuid():N}-missing-dir", "device.id"));
}
