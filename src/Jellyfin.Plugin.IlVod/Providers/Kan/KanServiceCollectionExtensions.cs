using System.Net;
using Jellyfin.Plugin.IlVod.Cache;
using Jellyfin.Plugin.IlVod.Configuration;
using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Providers.Kan.Source;
using Jellyfin.Plugin.IlVod.Providers.Kan.Transform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IlVod.Providers.Kan;

/// <summary>
/// Builds Kan 11's dependency graph and registers it as one <see cref="IContentProvider"/>.
/// Every factory here runs only once the channel's catalogue is first resolved — on a user's first
/// action — never at registration, since the data folder and configuration are not reachable yet.
/// </summary>
internal static class KanServiceCollectionExtensions
{
    private const string BrowseCacheFileName = "kan-cache.json";

    /// <summary>A setting of 0 (or less) would cache for no time at all, turning every open into a
    /// cold one — about forty requests for the show list — so it is floored.</summary>
    private const int MinimumCacheHours = 1;

    internal static void AddKanProvider(this IServiceCollection services, Func<string> dataFolder, Func<PluginConfiguration> configuration)
    {
        services.AddSingleton(KanProvider.Description);

        // Distinct wrapper types, as mako's and Reshet 13's, so DI tells the persisted catalogue
        // cache and the memory-only stream-link cache apart without a magic key (N2 vs. N3).
        services.AddSingleton(provider => new KanBrowseCache(
            new TimedCache(provider.GetRequiredService<IClock>(), PluginDataFolder.PathOf(dataFolder, BrowseCacheFileName))));
        services.AddSingleton(provider => new KanStreamLinkCache(new TimedCache(provider.GetRequiredService<IClock>())));

        services.AddSingleton<IKanHttp>(provider => new KanHttpClient(
            CreateHttpClient(),
            provider.GetRequiredService<ILogger<KanHttpClient>>()));

        services.AddSingleton(provider => new KanSource(
            provider.GetRequiredService<IKanHttp>(),
            provider.GetRequiredService<KanBrowseCache>().Value,
            () => LifetimesFrom(configuration()),
            provider.GetRequiredService<ILogger<KanSource>>()));

        // The only place Kan's fetching half and its converting half meet.
        services.AddSingleton<IContentProvider>(provider => new KanProvider(
            new SourceCatalog<KanShowIndex, KanShowPage, KanSeasonPage, KanStream>(
                provider.GetRequiredService<KanSource>(),
                new KanTransformer(),
                provider.GetRequiredService<KanStreamLinkCache>().Value,
                () => TimeSpan.FromMinutes(configuration().StreamLinkCacheMinutes))));
    }

    /// <summary>internal (not private): a test reaches this directly, through the test project's
    /// <c>InternalsVisibleTo</c> grant, to pin the floor without building a whole service graph.</summary>
    internal static KanLifetimes LifetimesFrom(PluginConfiguration configuration) => new(
        Catalogue: AtLeastOneHour(configuration.Kan.CatalogueCacheHours),
        Show: AtLeastOneHour(configuration.Kan.ShowCacheHours));

    private static TimeSpan AtLeastOneHour(int hours) => TimeSpan.FromHours(Math.Max(MinimumCacheHours, hours));

    /// <summary>The lobby is 2.2 MB as text and a tenth of that compressed. Redirects are followed:
    /// an episode page answers a 301 to its canonical path, and Redge a 302 to an edge node.</summary>
    private static HttpClient CreateHttpClient() => new(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })
    {
        Timeout = KanHttpClient.Timeout,
    };
}

/// <summary>Kan's persisted catalogue cache: the parsed show list and show pages, written to
/// <c>kan-cache.json</c> in the plugin's data folder so a restart does not refetch them.</summary>
internal sealed class KanBrowseCache
{
    public KanBrowseCache(TimedCache value)
    {
        Value = value;
    }

    public TimedCache Value { get; }
}

/// <summary>Kan's resolved-stream-link cache, built with no persistence path so a stream URL never
/// reaches disk (N3) — never give this type's registration a path.</summary>
internal sealed class KanStreamLinkCache
{
    public KanStreamLinkCache(TimedCache value)
    {
        Value = value;
    }

    public TimedCache Value { get; }
}
