using Jellyfin.Plugin.IlVod.Cache;
using Jellyfin.Plugin.IlVod.Providers.Mako;
using Jellyfin.Plugin.IlVod.Tests.Support;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Mako;

public class MakoConfigLoaderTests
{
    private const string AppVersion = "43.0";

    private static readonly string MainConfigJson = FixtureFile.ReadText("main-config.json");
    private static readonly string CoreConfigJson = FixtureFile.ReadText("core-config.json");
    private static readonly string NotesConfigJson = FixtureFile.ReadText("notes-config.json");

    private static readonly string MainConfigUrl = MakoUrls.MainConfigUrl(AppVersion);
    private static readonly string CoreConfigUrl = MakoConfig.CoreConfigUrlOf(MainConfigJson);
    private static readonly string NotesConfigUrl = MakoConfig.NotesUrlOf(CoreConfigJson);

    [Fact]
    public async Task LoadsTheThreeFilesInOrderAndParsesThem()
    {
        FakeMakoHttp http = SeededHttp();
        MakoConfigLoader loader = new(http, new TimedCache(new FakeClock()), () => AppVersion);

        MakoConfig config = await loader.LoadAsync(CancellationToken.None);

        Assert.Equal([MainConfigUrl, CoreConfigUrl, NotesConfigUrl], http.Requests);
        Assert.Equal("078E8ABF-12plus-C6550-androidF1A35D4", config.AppId);
    }

    [Fact]
    public async Task ASecondLoadInsideADayMakesNoRequest()
    {
        FakeMakoHttp http = SeededHttp();
        FakeClock clock = new();
        MakoConfigLoader loader = new(http, new TimedCache(clock), () => AppVersion);
        await loader.LoadAsync(CancellationToken.None);

        clock.Advance(TimeSpan.FromHours(23));
        MakoConfig config = await loader.LoadAsync(CancellationToken.None);

        Assert.Equal(3, http.Requests.Count);
        Assert.Equal("078E8ABF-12plus-C6550-androidF1A35D4", config.AppId);
    }

    [Fact]
    public async Task ConcurrentColdLoadsShareOneFetchChain()
    {
        FakeMakoHttp http = SeededHttp();
        TaskCompletionSource mainConfigGate = http.GateResponseTo(MainConfigUrl);
        MakoConfigLoader loader = new(http, new TimedCache(new FakeClock()), () => AppVersion);

        // Both calls start cold: the second reaches the loader's gate while the first is still
        // waiting on its (held-open) main-config request, proving they share one fetch rather
        // than racing mako for two.
        Task<MakoConfig> firstLoad = loader.LoadAsync(CancellationToken.None);
        Task<MakoConfig> secondLoad = loader.LoadAsync(CancellationToken.None);

        mainConfigGate.SetResult();
        MakoConfig firstConfig = await firstLoad;
        MakoConfig secondConfig = await secondLoad;

        Assert.Equal(3, http.Requests.Count);
        // MakoConfig's own record equality falls back to reference equality for PlaylistKeys (an
        // IReadOnlyList<string>, which List<string> does not override) — compare field by field.
        Assert.Equal(firstConfig.AppId, secondConfig.AppId);
        Assert.Equal(firstConfig.BaseUrlTemplate, secondConfig.BaseUrlTemplate);
        Assert.Equal(firstConfig.PlaylistUrlTemplate, secondConfig.PlaylistUrlTemplate);
        Assert.Equal(firstConfig.TokenUrl, secondConfig.TokenUrl);
        Assert.Equal(firstConfig.TokenKey, secondConfig.TokenKey);
        Assert.Equal(firstConfig.PlaylistKeys, secondConfig.PlaylistKeys);
    }

    [Fact]
    public async Task AfterForgetTheConfigIsFetchedAgain()
    {
        FakeMakoHttp http = SeededHttp();
        MakoConfigLoader loader = new(http, new TimedCache(new FakeClock()), () => AppVersion);
        await loader.LoadAsync(CancellationToken.None);

        loader.Forget();
        await loader.LoadAsync(CancellationToken.None);

        Assert.Equal(6, http.Requests.Count);
    }

    /// <summary>
    /// A document that cannot be parsed is not a config, so it never reaches the 24-hour cache:
    /// otherwise one HTML error page from mako would break every load for a whole day. The next
    /// load fetches again.
    /// </summary>
    [Fact]
    public async Task AConfigDocumentThatIsNotJsonIsNeverCachedSoTheNextLoadFetchesAgain()
    {
        FakeMakoHttp http = SeededHttp();
        http.Respond(MainConfigUrl, "<html><body>Access Denied</body></html>");
        MakoConfigLoader loader = new(http, new TimedCache(new FakeClock()), () => AppVersion);

        await Assert.ThrowsAsync<FormatException>(() => loader.LoadAsync(CancellationToken.None));
        Assert.Single(http.Requests);

        await Assert.ThrowsAsync<FormatException>(() => loader.LoadAsync(CancellationToken.None));
        Assert.Equal(2, http.Requests.Count);
    }

    /// <summary>The same for the last document in the chain: a notes config that parses but is
    /// missing its keys leaves nothing behind either, so no half-loaded chain can be served.</summary>
    [Fact]
    public async Task ANotesConfigWithoutKeysLeavesNothingCached()
    {
        FakeMakoHttp http = SeededHttp();
        http.Respond(NotesConfigUrl, """{ "notes": {} }""");
        MakoConfigLoader loader = new(http, new TimedCache(new FakeClock()), () => AppVersion);

        await Assert.ThrowsAsync<FormatException>(() => loader.LoadAsync(CancellationToken.None));
        Assert.Equal(3, http.Requests.Count);

        await Assert.ThrowsAsync<FormatException>(() => loader.LoadAsync(CancellationToken.None));
        Assert.Equal(6, http.Requests.Count);
    }

    private static FakeMakoHttp SeededHttp()
    {
        FakeMakoHttp http = new();
        http.Respond(MainConfigUrl, MainConfigJson);
        http.Respond(CoreConfigUrl, CoreConfigJson);
        http.Respond(NotesConfigUrl, NotesConfigJson);
        return http;
    }
}
