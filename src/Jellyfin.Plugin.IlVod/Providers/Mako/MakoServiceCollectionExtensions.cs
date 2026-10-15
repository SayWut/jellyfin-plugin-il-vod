using Jellyfin.Plugin.IlVod.Cache;
using Jellyfin.Plugin.IlVod.Configuration;
using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Providers.Mako.Source;
using Jellyfin.Plugin.IlVod.Providers.Mako.Transform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IlVod.Providers.Mako;

/// <summary>
/// Builds mako's dependency graph (spec §5.1) and registers it as one <see cref="IContentProvider"/>.
/// Every factory here runs only once the channel's catalogue is first resolved — on a user's first
/// action — never at registration, since the data folder and configuration are not reachable yet.
/// </summary>
internal static class MakoServiceCollectionExtensions
{
    private const string BrowseCacheFileName = "mako-cache.json";
    private const string DeviceIdentityFileName = "mako-device.id";

    /// <summary>A setting of 0 (or less) would cache the catalogue for no time at all, turning
    /// every channel open into a cold one — floored to the smallest lifetime that still means
    /// "cached" rather than trusting the settings page's number field unchecked.</summary>
    private const int MinimumCatalogueCacheHours = 1;

    /// <summary>The 15 s timeout applies from here down: <see cref="MakoConfigLoader"/>, <see cref="MakoPageSource"/>
    /// and <see cref="StreamResolver"/> all share the one <see cref="IMakoHttp"/> registered below.</summary>
    internal static void AddMakoProvider(this IServiceCollection services, Func<string> dataFolder, Func<PluginConfiguration> configuration)
    {
        services.AddSingleton(MakoProvider.Description);

        services.AddSingleton(provider => new RequestPacer(provider.GetRequiredService<IClock>(), Task.Delay));

        // Two distinct wrapper types, not two registrations of the same TimedCache keyed by a
        // string, so DI (and this file's readers) can tell "persisted browse cache" and
        // "memory-only stream-link cache" apart without a magic key (N2 vs. N3).
        services.AddSingleton(provider => new MakoBrowseCache(
            new TimedCache(provider.GetRequiredService<IClock>(), PluginDataFolder.PathOf(dataFolder, BrowseCacheFileName))));
        services.AddSingleton(provider => new MakoStreamLinkCache(new TimedCache(provider.GetRequiredService<IClock>())));

        services.AddSingleton<IMakoHttp>(provider => new MakoHttpClient(
            CreateHttpClient(),
            provider.GetRequiredService<RequestPacer>(),
            provider.GetRequiredService<ILogger<MakoHttpClient>>()));

        services.AddSingleton(provider => new MakoConfigLoader(
            provider.GetRequiredService<IMakoHttp>(),
            provider.GetRequiredService<MakoBrowseCache>().Value,
            () => configuration().AppVersion));

        services.AddSingleton(_ => new DeviceIdentity(PluginDataFolder.PathOf(dataFolder, DeviceIdentityFileName)));

        services.AddSingleton(provider => new StreamResolver(
            provider.GetRequiredService<IMakoHttp>(),
            provider.GetRequiredService<MakoConfigLoader>(),
            provider.GetRequiredService<DeviceIdentity>(),
            () => configuration().AppVersion,
            provider.GetRequiredService<ILogger<StreamResolver>>()));

        services.AddSingleton(provider => new MakoPageSource(
            provider.GetRequiredService<IMakoHttp>(),
            provider.GetRequiredService<MakoConfigLoader>(),
            provider.GetRequiredService<MakoBrowseCache>().Value,
            provider.GetRequiredService<ILogger<MakoPageSource>>()));

        services.AddSingleton(provider => new MakoSource(
            provider.GetRequiredService<MakoPageSource>(),
            provider.GetRequiredService<StreamResolver>(),
            () => CatalogueLifetimesFrom(configuration()),
            provider.GetRequiredService<ILogger<MakoSource>>()));

        // The only place mako's fetching half and its converting half meet.
        services.AddSingleton<IContentProvider>(provider => new MakoProvider(
            new SourceCatalog<MakoShowIndex, ShowPage, MakoSeasonPage, MakoResolvedStream>(
                provider.GetRequiredService<MakoSource>(),
                new MakoTransformer(),
                provider.GetRequiredService<MakoStreamLinkCache>().Value,
                () => CatalogueLifetimesFrom(configuration()).StreamLink)));
    }

    /// <summary>internal (not private): a test reaches this directly, through the test project's
    /// <c>InternalsVisibleTo</c> grant, to pin the floor without building a whole service graph.</summary>
    internal static CatalogueLifetimes CatalogueLifetimesFrom(PluginConfiguration configuration) => new(
        Catalogue: TimeSpan.FromHours(Math.Max(MinimumCatalogueCacheHours, configuration.CatalogueCacheHours)),
        ShowPage: TimeSpan.FromHours(configuration.ShowPageCacheHours),
        PastSeason: TimeSpan.FromDays(configuration.PastSeasonCacheDays),
        CurrentSeason: TimeSpan.FromHours(configuration.CurrentSeasonCacheHours),
        StreamLink: TimeSpan.FromMinutes(configuration.StreamLinkCacheMinutes));

    /// <summary>
    /// N4/N3: <see cref="System.Net.Http.SocketsHttpHandler.AllowAutoRedirect"/> is off (see
    /// <see cref="MakoHttpHandlerFactory"/>) and the timeout is <see cref="MakoHttpClient.Timeout"/>
    /// — the constant that class itself names for exactly this wiring.
    /// </summary>
    private static HttpClient CreateHttpClient() => new(MakoHttpHandlerFactory.CreateHandler())
    {
        Timeout = MakoHttpClient.Timeout,
    };
}

/// <summary>
/// mako's persisted browse-page cache (spec §5.3): mako's page text and its config files, written
/// to <c>mako-cache.json</c> in the plugin's data folder so a Jellyfin restart does not cause a
/// wave of refetches. A distinct type from <see cref="MakoStreamLinkCache"/> is what lets DI
/// resolve the right one without a magic string key.
/// </summary>
internal sealed class MakoBrowseCache
{
    public MakoBrowseCache(TimedCache value)
    {
        Value = value;
    }

    public TimedCache Value { get; }
}

/// <summary>
/// mako's resolved-stream-link cache (spec §5.3). Built over a <see cref="TimedCache"/> with no
/// persistence path, so a signed URL never reaches disk (N3) — never give this type's registration
/// a path the way <see cref="MakoBrowseCache"/>'s has one.
/// </summary>
internal sealed class MakoStreamLinkCache
{
    public MakoStreamLinkCache(TimedCache value)
    {
        Value = value;
    }

    public TimedCache Value { get; }
}
