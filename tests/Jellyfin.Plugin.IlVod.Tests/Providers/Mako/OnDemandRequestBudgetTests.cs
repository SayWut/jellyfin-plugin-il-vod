using Jellyfin.Plugin.IlVod.Cache;
using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Providers.Mako;
using Jellyfin.Plugin.IlVod.Providers.Mako.Source;
using Jellyfin.Plugin.IlVod.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Mako;

/// <summary>
/// N1's definition of done (spec §2): one <see cref="FakeMakoHttp"/> wired through the real
/// <see cref="MakoConfigLoader"/>, <see cref="MakoPageSource"/>, <see cref="StreamResolver"/> and
/// <see cref="MakoSource"/> (through <see cref="MakoTestCatalog"/>), counting requests after every step of a user session — browsing a
/// show, its current season, a past season, and pressing play — to prove that only what each
/// action actually needs is ever asked of mako, nothing ahead of it and nothing repeated inside a
/// cache's lifetime.
/// </summary>
public class OnDemandRequestBudgetTests
{
    private const string AppVersion = "43.0";

    // The show's own landing path, which is NOT the same as whichever season page it happens to
    // display (that is DisplayedSeasonPath below). Serving the show-page fixture's JSON at this
    // distinct url is what makes the "current season" row of the table below a genuine test of
    // MakoSource.Remember-ing under a different cache key, rather than a same-key overwrite that
    // would pass even if nothing were remembered at all.
    private const string ShemeshPagePath = "/mako-vod-keshet/shemesh";
    private const string DisplayedSeasonPath = "/mako-vod-keshet/shemesh-s6";
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

    private static readonly string MainConfigJson = FixtureFile.ReadText("main-config.json");
    private static readonly string CoreConfigJson = FixtureFile.ReadText("core-config.json");
    private static readonly string NotesConfigJson = FixtureFile.ReadText("notes-config.json");
    private static readonly string ShowPageJson = FixtureFile.ReadText("show-page-shemesh.json");
    private static readonly string SeasonOnePageJson = FixtureFile.ReadText("season-page-shemesh-s1.json");
    private static readonly string MasterPlaylistText = FixtureFile.ReadText("master.m3u8");
    private static readonly string MenuJson = FixtureFile.ReadText("genre-menu.json");
    private static readonly string ComedyJson = FixtureFile.ReadText("genre-list-comedy.json");
    private static readonly string DramaJson = FixtureFile.ReadText("genre-list-drama.json");
    private static readonly string NewsJson = FixtureFile.ReadText("provider-list-news.json");
    private static readonly string FullIndexJson = FixtureFile.ReadText("mako-index-all.json");
    private static readonly string EmptyListJson = """{"items":[]}""";

    private static readonly string MainConfigUrl = MakoUrls.MainConfigUrl(AppVersion);
    private static readonly string CoreConfigUrl = MakoConfig.CoreConfigUrlOf(MainConfigJson);
    private static readonly string NotesConfigUrl = MakoConfig.NotesUrlOf(CoreConfigJson);
    private static readonly MakoConfig ParsedConfig = MakoConfig.Parse(MainConfigJson, CoreConfigJson, NotesConfigJson);

    private static readonly string ShowPageUrl = MakoUrls.Page(ParsedConfig.BaseUrlTemplate, ShemeshPagePath);
    private static readonly string SeasonOneUrl = MakoUrls.Page(ParsedConfig.BaseUrlTemplate, SeasonOnePagePath);

    private static readonly EpisodeKey ProtectedEpisode =
        new(ProtectedEpisodeVcmId, ProtectedEpisodeChannelId, ProtectedEpisodeGalleryChannelId);
    private static readonly string PlaylistUrl = MakoUrls.Playlist(
        ParsedConfig.PlaylistUrlTemplate, ProtectedEpisode.VcmId, ProtectedEpisode.ChannelId, ProtectedEpisode.GalleryChannelId);
    private static readonly string TokenUrl = ParsedConfig.TokenUrl;
    private static readonly string FinalMediaUrl = $"{MediaUrl}?{DecodedTicket}";

    // master.m3u8's best variant ("../850/…/index_850.m3u8") resolved against the master's URL:
    // the one extra CDN request per play, which measures the episode's length.
    private const string BestVariantUrl = "https://cdn.example.test/850/hdntl=exp=1790000000~acl=%2f*~data=hdntl~hmac=0000/index_850.m3u8";

    [Fact]
    public async Task TheRequestBudgetMatchesEveryStepOfTheOnDemandTable()
    {
        FakeMakoHttp http = SeededHttp();
        ICatalog catalog = await CreateCatalogAsync(http);

        // | Open the channel (cold catalogue) | 8 (full list + menu + 5 genre lists + החדשות) |
        await catalog.GetShowsAsync(CancellationToken.None);
        Assert.Equal(8, BrowseHostRequestCount(http));

        // | Open the channel again (fresh catalogue) | 8, unchanged |
        await catalog.GetShowsAsync(CancellationToken.None);
        Assert.Equal(8, BrowseHostRequestCount(http));

        // | Open a show | 9 (the show page) |
        await catalog.GetSeasonsAsync(ShemeshPagePath, CancellationToken.None);
        Assert.Equal(9, BrowseHostRequestCount(http));

        // | Open the season the show page displayed | 9, unchanged |
        await catalog.GetEpisodesAsync(ShemeshPagePath, DisplayedSeasonPath, CancellationToken.None);
        Assert.Equal(9, BrowseHostRequestCount(http));

        // | Open another season | 10 |
        await catalog.GetEpisodesAsync(ShemeshPagePath, SeasonOnePagePath, CancellationToken.None);
        Assert.Equal(10, BrowseHostRequestCount(http));

        // | GetEpisodesAsync(season 1) again | 10 |
        await catalog.GetEpisodesAsync(ShemeshPagePath, SeasonOnePagePath, CancellationToken.None);
        Assert.Equal(10, BrowseHostRequestCount(http));

        // | GetSeasonsAsync(show) again | 10 |
        await catalog.GetSeasonsAsync(ShemeshPagePath, CancellationToken.None);
        Assert.Equal(10, BrowseHostRequestCount(http));

        // | Press play (protected) | 11 on www + 1 POST token + 2 CDN GETs |
        StreamLink link = await catalog.GetStreamLinkAsync(ProtectedEpisode.ToString(), CancellationToken.None);
        Assert.Equal(11, BrowseHostRequestCount(http));
        Assert.Equal(1, RequestCountTo(http, TokenUrl));
        Assert.Equal(1, RequestCountTo(http, FinalMediaUrl));
        Assert.Equal(1, RequestCountTo(http, BestVariantUrl));
        Assert.Equal(FinalMediaUrl, link.Url);

        // The token endpoint is POSTed to; every other request (pages, playlist, the CDN) is a GET.
        Assert.Contains(http.Calls, call => call.Method == "POST" && call.Url == TokenUrl);
        Assert.All(http.Calls.Where(call => call.Url != TokenUrl), call => Assert.Equal("GET", call.Method));

        // | same episode again within 5 min | unchanged |
        StreamLink again = await catalog.GetStreamLinkAsync(ProtectedEpisode.ToString(), CancellationToken.None);
        Assert.Equal(11, BrowseHostRequestCount(http));
        Assert.Equal(1, RequestCountTo(http, TokenUrl));
        Assert.Equal(1, RequestCountTo(http, FinalMediaUrl));
        Assert.Equal(1, RequestCountTo(http, BestVariantUrl));
        Assert.Equal(link, again);
    }

    [Fact]
    public async Task NothingIsEverRequestedForSeasonsNobodyOpened()
    {
        FakeMakoHttp http = SeededHttp();
        ICatalog catalog = await CreateCatalogAsync(http);

        await catalog.GetSeasonsAsync(ShemeshPagePath, CancellationToken.None);
        await catalog.GetEpisodesAsync(ShemeshPagePath, SeasonOnePagePath, CancellationToken.None);

        foreach (string untouchedSeasonSlug in new[] { "shemesh-s2", "shemesh-s3", "shemesh-s4", "shemesh-s5" })
        {
            Assert.DoesNotContain(http.Requests, url => url.Contains(untouchedSeasonSlug, StringComparison.Ordinal));
        }

        // Positive control: the same matcher style DOES find the season that was actually opened,
        // so the assertions above are proven to be checking something real rather than passing
        // vacuously because the matcher itself never matches anything.
        Assert.Contains(http.Requests, url => url.Contains("shemesh-s1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NothingIsEverRequestedForShowsNobodyOpened()
    {
        FakeMakoHttp http = SeededHttp();
        ICatalog catalog = await CreateCatalogAsync(http);

        await catalog.GetShowsAsync(CancellationToken.None);

        Assert.DoesNotContain(http.Requests, url => url.Contains("/mako-vod-keshet/", StringComparison.Ordinal));

        // Positive control: the same matcher style DOES find the index request GetShowsAsync
        // itself makes, so the assertion above is proven to be checking something real rather
        // than passing vacuously because the matcher itself never matches anything.
        Assert.Contains(http.Requests, url => url.Contains("mako-vod-index", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASeasonOpenNeverTurnsIntoAnIndexBurst()
    {
        FakeMakoHttp http = SeededHttp();
        ICatalog catalog = await CreateCatalogAsync(http);

        await catalog.GetEpisodesAsync(ShemeshPagePath, SeasonOnePagePath, CancellationToken.None);

        Assert.DoesNotContain(http.Requests, url => url.Contains("mako-vod-index", StringComparison.Ordinal));

        // Positive control: the same matcher style DOES find the one request this test's own
        // GetEpisodesAsync call makes, so the assertion above is proven to be checking something
        // real rather than passing vacuously because the matcher itself never matches anything.
        Assert.Contains(http.Requests, url => url.Contains("shemesh-s1", StringComparison.Ordinal));
    }

    /// <summary>The menu entries that are not category lists: none of them is ever asked for, in
    /// any step of a session — podcast and ערוץ 24 shows are not even in mako's full list.</summary>
    [Fact]
    public async Task TheOtherProviderListsTheAbcListAndPlaylistsAreNeverRequested()
    {
        FakeMakoHttp http = SeededHttp();
        ICatalog catalog = await CreateCatalogAsync(http);

        await catalog.GetShowsAsync(CancellationToken.None);
        await catalog.GetSeasonsAsync(ShemeshPagePath, CancellationToken.None);
        await catalog.GetEpisodesAsync(ShemeshPagePath, SeasonOnePagePath, CancellationToken.None);
        await catalog.GetStreamLinkAsync(ProtectedEpisode.ToString(), CancellationToken.None);

        foreach (string excludedPath in new[]
        {
            "/mako-vod-index?filter=provider&vcmId=d5aae64655ea0810VgnVCM100000700a10acRCRD", // פודקאסטים
            "/mako-vod-index?filter=provider&vcmId=3377c13070733210VgnVCM2000002a0c10acRCRD", // ערוץ 24
            "/mako-vod-index?filter=ABC_Programs_Slider",
            "/mako-vod-index?filter=playlists",
        })
        {
            Assert.DoesNotContain(MakoUrls.Page(ParsedConfig.BaseUrlTemplate, excludedPath), http.Requests);
        }

        // Positive control: the allowlisted provider list IS asked for, built the very same way.
        Assert.Contains(
            MakoUrls.Page(ParsedConfig.BaseUrlTemplate, "/mako-vod-index?filter=provider&vcmId=ee06c13070733210VgnVCM2000002a0c10acRCRD"),
            http.Requests);
    }

    /// <summary>The full show list, the genre menu and its six category lists (comedy, drama and
    /// החדשות carry the real fixtures; the other three real genres answer one throwaway
    /// placeholder show each, never an empty list — mako's real genres are never empty, spec
    /// §6.2) — same seeding as <c>MakoCatalogTests</c> — plus the show/season/playlist/token/CDN
    /// fixtures the rest of this test's steps need.</summary>
    private static FakeMakoHttp SeededHttp()
    {
        FakeMakoHttp http = new();
        http.Respond(MakoUrls.Page(ParsedConfig.BaseUrlTemplate, MakoIndexPaths.FullIndex), FullIndexJson);
        http.Respond(MakoUrls.Page(ParsedConfig.BaseUrlTemplate, MakoIndexPaths.GenreMenu), MenuJson);
        foreach (Genre genre in MakoIndex.ParseGenres(MenuJson))
        {
            string listJson = genre.Name switch
            {
                "קומדיה" => ComedyJson,
                "דרמה" => DramaJson,
                "החדשות" => NewsJson,
                "ריאליטי" => PlaceholderListJson(genre.Name, "/mako-vod-keshet/placeholder-1"),
                "דוקומנטרי" => PlaceholderListJson(genre.Name, "/mako-vod-keshet/placeholder-2"),
                "בישול" => PlaceholderListJson(genre.Name, "/mako-vod-keshet/placeholder-3"),
                _ => EmptyListJson,
            };
            http.Respond(MakoUrls.Page(ParsedConfig.BaseUrlTemplate, genre.PagePath), listJson);
        }

        http.Respond(ShowPageUrl, ShowPageJson);
        http.Respond(SeasonOneUrl, SeasonOnePageJson);
        http.Respond(PlaylistUrl, EncryptedProtectedPlaylist());
        http.Respond(TokenUrl, EncryptedTokenResponse());
        http.Respond(FinalMediaUrl, MasterPlaylistText);
        http.Respond(BestVariantUrl, "#EXTM3U\n#EXTINF:6.0,\n00.ts\n#EXT-X-ENDLIST\n");
        return http;
    }

    /// <summary>One throwaway show for a genre this suite carries no real fixture for — never
    /// <see cref="EmptyListJson"/>, since §6.2 treats a real genre naming no show as a failure.</summary>
    private static string PlaceholderListJson(string genreName, string pagePath) =>
        $$"""{"items":[{"itemVcmId":"x","title":"{{genreName}} placeholder","pageUrl":"{{pagePath}}","pic":null}]}""";

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

    private static async Task<ICatalog> CreateCatalogAsync(FakeMakoHttp http)
    {
        FakeClock clock = new();
        TimedCache pageCache = new(clock);
        TimedCache streamLinkCache = new(clock);
        MakoConfigLoader config = await WarmedConfigLoaderAsync(http);
        MakoPageSource pages = new(http, config, pageCache, new RecordingLogger<MakoPageSource>());
        StreamResolver resolver = new(http, config, CreateDevice(), () => AppVersion, new RecordingLogger<StreamResolver>());

        return MakoTestCatalog.Over(pages, resolver, streamLinkCache);
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
