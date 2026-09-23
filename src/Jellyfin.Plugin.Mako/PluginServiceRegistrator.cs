using Jellyfin.Plugin.Mako.Cache;
using Jellyfin.Plugin.Mako.Catalog;
using Jellyfin.Plugin.Mako.Configuration;
using Jellyfin.Plugin.Mako.MakoApi;
using Jellyfin.Plugin.Mako.Transcoding;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Mako;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Plugin.Instance does not exist yet at registration time — every read of it below is
        // deferred into a factory delegate, inside AddMakoServices, never done here.
        serviceCollection.AddMakoServices(
            dataFolder: () => Plugin.Instance.DataFolderPath,
            configuration: () => Plugin.Instance.Configuration);

        // Jellyfin's global -probesize setting buffers mako's unidentified data PID in RAM
        // until the phone aborts playback (see docs/spec.md §4.3). Wrapping the transcode
        // manager lets the plugin rewrite the probe size for its own streams only. No logger
        // exists yet at this point in startup, so whether it succeeded is recorded in
        // ProbeSizeLimiterStatus.IsInstalled for MakoChannel to report on the next play.
        _ = TranscodeManagerDecoration.Apply(serviceCollection);
    }
}

/// <summary>
/// Builds the live mako catalogue's dependency graph (spec §5.1). Kept separate from
/// <see cref="PluginServiceRegistrator"/> itself so a test can call it directly, over a temporary
/// data folder and a throwaway configuration, without <see cref="Plugin.Instance"/> ever having to
/// exist — see <c>PluginServiceRegistratorTests</c>.
/// </summary>
internal static class MakoServiceCollectionExtensions
{
    private const string BrowseCacheFileName = "mako-cache.json";
    private const string DeviceIdentityFileName = "device.id";

    /// <summary>The 15 s timeout applies from here down: <see cref="MakoConfigLoader"/>, <see cref="MakoPageSource"/>
    /// and <see cref="StreamResolver"/> all share the one <see cref="IMakoHttp"/> registered below.</summary>
    internal static void AddMakoServices(this IServiceCollection services, Func<string> dataFolder, Func<PluginConfiguration> configuration)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton(provider => new RequestPacer(provider.GetRequiredService<IClock>(), Task.Delay));

        // Two distinct wrapper types, not two registrations of the same TimedCache keyed by a
        // string, so DI (and this file's readers) can tell "persisted browse cache" and
        // "memory-only stream-link cache" apart without a magic key (N2 vs. N3).
        services.AddSingleton(provider => new BrowseCache(new TimedCache(provider.GetRequiredService<IClock>(), BrowseCachePathIn(dataFolder))));
        services.AddSingleton(provider => new StreamLinkCache(new TimedCache(provider.GetRequiredService<IClock>())));

        services.AddSingleton<IMakoHttp>(provider => new MakoHttpClient(
            CreateHttpClient(),
            provider.GetRequiredService<RequestPacer>(),
            provider.GetRequiredService<ILogger<MakoHttpClient>>()));

        services.AddSingleton(provider => new MakoConfigLoader(
            provider.GetRequiredService<IMakoHttp>(),
            provider.GetRequiredService<BrowseCache>().Value,
            () => configuration().AppVersion));

        services.AddSingleton(_ => new DeviceIdentity(DeviceIdentityPathIn(dataFolder)));

        services.AddSingleton(provider => new StreamResolver(
            provider.GetRequiredService<IMakoHttp>(),
            provider.GetRequiredService<MakoConfigLoader>(),
            provider.GetRequiredService<DeviceIdentity>(),
            () => configuration().AppVersion,
            provider.GetRequiredService<ILogger<StreamResolver>>()));

        services.AddSingleton(provider => new MakoPageSource(
            provider.GetRequiredService<IMakoHttp>(),
            provider.GetRequiredService<MakoConfigLoader>(),
            provider.GetRequiredService<BrowseCache>().Value,
            provider.GetRequiredService<ILogger<MakoPageSource>>()));

        services.AddSingleton<ICatalog>(provider => new MakoCatalog(
            provider.GetRequiredService<MakoPageSource>(),
            provider.GetRequiredService<StreamResolver>(),
            provider.GetRequiredService<StreamLinkCache>().Value,
            () => CatalogueLifetimesFrom(configuration())));

        // Lazy, not ICatalog: Jellyfin constructs every IChannel before any plugin instance
        // exists (see MakoChannel's own remarks), and building the catalogue reads the data
        // folder and the configuration — both of which throw until Plugin.Instance is set.
        services.AddSingleton<IChannel>(provider => new MakoChannel(
            new Lazy<ICatalog>(provider.GetRequiredService<ICatalog>),
            provider.GetRequiredService<ILogger<MakoChannel>>()));
    }

    /// <summary>
    /// N4/N3: <see cref="System.Net.Http.SocketsHttpHandler.AllowAutoRedirect"/> is off (see
    /// <see cref="MakoHttpHandlerFactory"/>) and the timeout is <see cref="MakoHttpClient.Timeout"/>
    /// — the constant that class itself names for exactly this wiring.
    /// </summary>
    private static HttpClient CreateHttpClient() => new(MakoHttpHandlerFactory.CreateHandler())
    {
        Timeout = MakoHttpClient.Timeout,
    };

    private static CatalogueLifetimes CatalogueLifetimesFrom(PluginConfiguration configuration) => new(
        Catalogue: TimeSpan.FromHours(configuration.CatalogueCacheHours),
        ShowPage: TimeSpan.FromHours(configuration.ShowPageCacheHours),
        PastSeason: TimeSpan.FromDays(configuration.PastSeasonCacheDays),
        CurrentSeason: TimeSpan.FromHours(configuration.CurrentSeasonCacheHours),
        StreamLink: TimeSpan.FromMinutes(configuration.StreamLinkCacheMinutes));

    private static string BrowseCachePathIn(Func<string> dataFolder) => Path.Combine(EnsuredDataFolder(dataFolder), BrowseCacheFileName);

    private static string DeviceIdentityPathIn(Func<string> dataFolder) => Path.Combine(EnsuredDataFolder(dataFolder), DeviceIdentityFileName);

    /// <summary>
    /// Creates the plugin's data folder if it does not already exist yet, tolerant of failure —
    /// exactly like the best-effort persistence <see cref="TimedCache"/> and
    /// <see cref="DeviceIdentity"/> already fall back to on their own, a folder that cannot be
    /// created must not stop the plugin from loading.
    /// </summary>
    private static string EnsuredDataFolder(Func<string> dataFolder)
    {
        string path = dataFolder();
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best-effort only: TimedCache.Write and DeviceIdentity.Get already tolerate an
            // unwritable location themselves.
        }

        return path;
    }
}

/// <summary>
/// The persisted browse-page cache (spec §5.3): mako's page text and its config files, written to
/// <c>mako-cache.json</c> in the plugin's data folder so a Jellyfin restart does not cause a wave
/// of refetches. A distinct type from <see cref="StreamLinkCache"/> is what lets DI resolve the
/// right one without a magic string key.
/// </summary>
internal sealed class BrowseCache
{
    public BrowseCache(TimedCache value)
    {
        Value = value;
    }

    public TimedCache Value { get; }
}

/// <summary>
/// The resolved-stream-link cache (spec §5.3). Built over a <see cref="TimedCache"/> with no
/// persistence path, so a signed URL never reaches disk (N3) — never give this type's registration
/// a path the way <see cref="BrowseCache"/>'s has one.
/// </summary>
internal sealed class StreamLinkCache
{
    public StreamLinkCache(TimedCache value)
    {
        Value = value;
    }

    public TimedCache Value { get; }
}
