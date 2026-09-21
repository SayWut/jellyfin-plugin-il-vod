using Jellyfin.Plugin.Mako.Cache;
using Jellyfin.Plugin.Mako.MakoApi;
using Jellyfin.Plugin.Mako.Tests.Support;

namespace Jellyfin.Plugin.Mako.Tests.Mako;

public class MakoPageSourceTests
{
    private const string AppVersion = "43.0";
    private const string ShowPagePath = "/mako-vod-keshet/shemesh-s6";

    private static readonly TimeSpan ShowPageLifetime = TimeSpan.FromHours(12);

    private static readonly string MainConfigJson = FixtureFile.ReadText("main-config.json");
    private static readonly string CoreConfigJson = FixtureFile.ReadText("core-config.json");
    private static readonly string NotesConfigJson = FixtureFile.ReadText("notes-config.json");
    private static readonly string ShowPageJson = FixtureFile.ReadText("show-page-shemesh.json");

    private static readonly string MainConfigUrl = MakoUrls.MainConfigUrl(AppVersion);
    private static readonly string CoreConfigUrl = MakoConfig.CoreConfigUrlOf(MainConfigJson);
    private static readonly string NotesConfigUrl = MakoConfig.NotesUrlOf(CoreConfigJson);
    private static readonly MakoConfig ParsedConfig = MakoConfig.Parse(MainConfigJson, CoreConfigJson, NotesConfigJson);

    private static readonly string ShowPageUrl = MakoUrls.Page(ParsedConfig.BaseUrlTemplate, ShowPagePath);

    [Fact]
    public async Task TheFirstReadFetchesAndTheSecondDoesNot()
    {
        FakeMakoHttp http = new();
        http.Respond(ShowPageUrl, ShowPageJson);
        MakoPageSource pages = await CreatePageSourceAsync(http);

        string first = await pages.GetAsync(ShowPagePath, _ => ShowPageLifetime, CancellationToken.None);
        string second = await pages.GetAsync(ShowPagePath, _ => ShowPageLifetime, CancellationToken.None);

        Assert.Equal(ShowPageJson, first);
        Assert.Equal(ShowPageJson, second);
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task AnExpiredPageIsFetchedAgain()
    {
        FakeClock clock = new();
        FakeMakoHttp http = new();
        http.Respond(ShowPageUrl, ShowPageJson);
        MakoPageSource pages = await CreatePageSourceAsync(http, clock);
        await pages.GetAsync(ShowPagePath, _ => ShowPageLifetime, CancellationToken.None);

        clock.Advance(ShowPageLifetime + TimeSpan.FromSeconds(1));
        await pages.GetAsync(ShowPagePath, _ => ShowPageLifetime, CancellationToken.None);

        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public async Task TheLifetimeIsDecidedFromThePageItself()
    {
        FakeMakoHttp http = new();
        http.Respond(ShowPageUrl, ShowPageJson);
        MakoPageSource pages = await CreatePageSourceAsync(http);
        string? jsonSeenByLifetimeOf = null;

        await pages.GetAsync(
            ShowPagePath,
            fetchedJson =>
            {
                jsonSeenByLifetimeOf = fetchedJson;
                return ShowPageLifetime;
            },
            CancellationToken.None);

        Assert.Equal(ShowPageJson, jsonSeenByLifetimeOf);
    }

    [Fact]
    public async Task AStaleCopyIsServedWhenMakoChallenges()
    {
        FakeClock clock = new();
        FakeMakoHttp http = new();
        http.Respond(ShowPageUrl, ShowPageJson);
        RecordingLogger<MakoPageSource> logger = new();
        MakoPageSource pages = await CreatePageSourceAsync(http, clock, logger);
        await pages.GetAsync(ShowPagePath, _ => ShowPageLifetime, CancellationToken.None);
        clock.Advance(ShowPageLifetime + TimeSpan.FromSeconds(1));
        http.Challenge(ShowPageUrl);

        string stale = await pages.GetAsync(ShowPagePath, _ => ShowPageLifetime, CancellationToken.None);

        Assert.Equal(ShowPageJson, stale);
        Assert.Single(logger.Warnings);
    }

    [Fact]
    public async Task AStaleCopyIsServedWhenMakoRequestFails()
    {
        FakeClock clock = new();
        FakeMakoHttp http = new();
        http.Respond(ShowPageUrl, ShowPageJson);
        RecordingLogger<MakoPageSource> logger = new();
        MakoPageSource pages = await CreatePageSourceAsync(http, clock, logger);
        await pages.GetAsync(ShowPagePath, _ => ShowPageLifetime, CancellationToken.None);
        clock.Advance(ShowPageLifetime + TimeSpan.FromSeconds(1));
        http.Fail(ShowPageUrl);

        string stale = await pages.GetAsync(ShowPagePath, _ => ShowPageLifetime, CancellationToken.None);

        Assert.Equal(ShowPageJson, stale);
        Assert.Single(logger.Warnings);
    }

    [Fact]
    public async Task WithNoCopyAtAllTheChallengeSurfaces()
    {
        FakeMakoHttp http = new();
        http.Challenge(ShowPageUrl);
        MakoPageSource pages = await CreatePageSourceAsync(http);

        await Assert.ThrowsAsync<BotChallengeException>(
            () => pages.GetAsync(ShowPagePath, _ => ShowPageLifetime, CancellationToken.None));
    }

    [Fact]
    public async Task ARememberedPageIsServedWithoutARequest()
    {
        FakeMakoHttp http = new();
        MakoPageSource pages = await CreatePageSourceAsync(http);

        pages.Remember(ShowPagePath, ShowPageJson, ShowPageLifetime);
        string served = await pages.GetAsync(ShowPagePath, _ => ShowPageLifetime, CancellationToken.None);

        Assert.Equal(ShowPageJson, served);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task TryGetCachedIsFalseWhenNothingIsCached()
    {
        FakeMakoHttp http = new();
        MakoPageSource pages = await CreatePageSourceAsync(http);

        bool found = pages.TryGetCached(ShowPagePath, out string? json);

        Assert.False(found);
        Assert.Null(json);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task TryGetCachedReturnsAStaleCopyWithoutARequest()
    {
        FakeClock clock = new();
        FakeMakoHttp http = new();
        http.Respond(ShowPageUrl, ShowPageJson);
        MakoPageSource pages = await CreatePageSourceAsync(http, clock);
        await pages.GetAsync(ShowPagePath, _ => ShowPageLifetime, CancellationToken.None);
        clock.Advance(ShowPageLifetime + TimeSpan.FromSeconds(1));

        bool found = pages.TryGetCached(ShowPagePath, out string? json);

        Assert.True(found);
        Assert.Equal(ShowPageJson, json);
        Assert.Single(http.Requests);
    }

    /// <summary>N4: at most one request to the bot-protected host at a time — two overlapping
    /// reads of the same uncached page must coalesce into one request.</summary>
    [Fact]
    public async Task ConcurrentReadsOfAnUncachedPageShareOneRequest()
    {
        FakeMakoHttp http = new();
        TaskCompletionSource gate = http.GateResponseTo(ShowPageUrl);
        http.Respond(ShowPageUrl, ShowPageJson);
        MakoPageSource pages = await CreatePageSourceAsync(http);

        Task<string> first = pages.GetAsync(ShowPagePath, _ => ShowPageLifetime, CancellationToken.None);
        Task<string> second = pages.GetAsync(ShowPagePath, _ => ShowPageLifetime, CancellationToken.None);
        gate.SetResult();
        string[] results = await Task.WhenAll(first, second);

        Assert.Equal(ShowPageJson, results[0]);
        Assert.Equal(ShowPageJson, results[1]);
        Assert.Single(http.Requests);
    }

    /// <summary>A failed in-flight fetch must not poison the coalescing entry forever: once it
    /// settles (with a failure both waiters observe), the entry is cleared and a later call
    /// dispatches its own fresh request rather than being silently stuck.</summary>
    [Fact]
    public async Task AFailedInFlightFetchDoesNotPoisonLaterCalls()
    {
        FakeMakoHttp http = new();
        TaskCompletionSource gate = http.GateResponseTo(ShowPageUrl);
        http.Fail(ShowPageUrl);
        MakoPageSource pages = await CreatePageSourceAsync(http);

        Task<string> first = pages.GetAsync(ShowPagePath, _ => ShowPageLifetime, CancellationToken.None);
        Task<string> second = pages.GetAsync(ShowPagePath, _ => ShowPageLifetime, CancellationToken.None);
        gate.SetResult();

        await Assert.ThrowsAsync<MakoRequestException>(() => first);
        await Assert.ThrowsAsync<MakoRequestException>(() => second);
        Assert.Single(http.Requests);

        await Assert.ThrowsAsync<MakoRequestException>(
            () => pages.GetAsync(ShowPagePath, _ => ShowPageLifetime, CancellationToken.None));

        Assert.Equal(2, http.Requests.Count);
    }

    private static async Task<MakoPageSource> CreatePageSourceAsync(
        FakeMakoHttp http,
        IClock? clock = null,
        RecordingLogger<MakoPageSource>? logger = null)
    {
        IClock actualClock = clock ?? new FakeClock();
        TimedCache pageCache = new(actualClock);
        MakoConfigLoader config = await WarmedConfigLoaderAsync(http, actualClock);
        return new MakoPageSource(http, config, pageCache, logger ?? new RecordingLogger<MakoPageSource>());
    }

    /// <summary>
    /// Loads the config chain through a throwaway <see cref="FakeMakoHttp"/> into a cache shared
    /// with the returned loader, so tests exercising <see cref="MakoPageSource"/> never see a
    /// config request land on <paramref name="http"/> — matching how a real plugin's config is
    /// already 24h-fresh by the time a user opens a folder.
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
}
