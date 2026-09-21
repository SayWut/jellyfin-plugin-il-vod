using Jellyfin.Plugin.Mako.Cache;
using Jellyfin.Plugin.Mako.MakoApi;
using Jellyfin.Plugin.Mako.Tests.Support;

namespace Jellyfin.Plugin.Mako.Tests.Mako;

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

    private static FakeMakoHttp SeededHttp()
    {
        FakeMakoHttp http = new();
        http.Respond(MainConfigUrl, MainConfigJson);
        http.Respond(CoreConfigUrl, CoreConfigJson);
        http.Respond(NotesConfigUrl, NotesConfigJson);
        return http;
    }
}
