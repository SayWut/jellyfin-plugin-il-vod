using System.Net;
using Jellyfin.Plugin.IlVod.Cache;
using Jellyfin.Plugin.IlVod.Configuration;
using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Providers.Reshet13.Source;
using Jellyfin.Plugin.IlVod.Providers.Reshet13.Transform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IlVod.Providers.Reshet13;

/// <summary>
/// Builds Reshet 13's dependency graph and registers it as one <see cref="IContentProvider"/>.
/// Every factory here runs only once the channel's catalogue is first resolved — on a user's first
/// action — never at registration, since the data folder and configuration are not reachable yet.
/// </summary>
internal static class Reshet13ServiceCollectionExtensions
{
    private const string BrowseCacheFileName = "reshet13-cache.json";

    /// <summary>A setting of 0 (or less) would cache for no time at all, turning every open into a
    /// cold one — floored rather than trusting the settings page's number field unchecked.</summary>
    private const int MinimumCacheHours = 1;

    internal static void AddReshet13Provider(this IServiceCollection services, Func<string> dataFolder, Func<PluginConfiguration> configuration)
    {
        services.AddSingleton(Reshet13Provider.Description);

        // Distinct wrapper types, as mako's, so DI tells the persisted catalogue cache and the
        // memory-only stream-link cache apart without a magic key (N2 vs. N3).
        services.AddSingleton(provider => new Reshet13BrowseCache(
            new TimedCache(provider.GetRequiredService<IClock>(), PluginDataFolder.PathOf(dataFolder, BrowseCacheFileName))));
        services.AddSingleton(provider => new Reshet13StreamLinkCache(new TimedCache(provider.GetRequiredService<IClock>())));

        services.AddSingleton<IReshet13Http>(provider => new Reshet13HttpClient(
            CreateHttpClient(),
            provider.GetRequiredService<ILogger<Reshet13HttpClient>>()));

        // One client, so every request shares the one anonymous session.
        services.AddSingleton(provider => new KalturaOttClient(
            provider.GetRequiredService<IReshet13Http>(),
            provider.GetRequiredService<ILogger<KalturaOttClient>>()));

        services.AddSingleton(provider => new Reshet13Source(
            provider.GetRequiredService<KalturaOttClient>(),
            provider.GetRequiredService<IReshet13Http>(),
            provider.GetRequiredService<Reshet13BrowseCache>().Value,
            () => LifetimesFrom(configuration()),
            provider.GetRequiredService<ILogger<Reshet13Source>>()));

        // The only place Reshet 13's fetching half and its converting half meet.
        services.AddSingleton<IContentProvider>(provider => new Reshet13Provider(
            new SourceCatalog<Reshet13ShowIndex, Reshet13ShowPage, Reshet13SeasonPage, Reshet13Stream>(
                provider.GetRequiredService<Reshet13Source>(),
                new Reshet13Transformer(),
                provider.GetRequiredService<Reshet13StreamLinkCache>().Value,
                () => TimeSpan.FromMinutes(configuration().StreamLinkCacheMinutes))));
    }

    /// <summary>internal (not private): a test reaches this directly, through the test project's
    /// <c>InternalsVisibleTo</c> grant, to pin the floor without building a whole service graph.</summary>
    internal static Reshet13Lifetimes LifetimesFrom(PluginConfiguration configuration) => new(
        Catalogue: AtLeastOneHour(configuration.Reshet13.CatalogueCacheHours),
        Show: AtLeastOneHour(configuration.Reshet13.ShowCacheHours));

    private static TimeSpan AtLeastOneHour(int hours) => TimeSpan.FromHours(Math.Max(MinimumCacheHours, hours));

    private static HttpClient CreateHttpClient() => new(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })
    {
        Timeout = Reshet13HttpClient.Timeout,
    };
}

/// <summary>Reshet 13's persisted catalogue cache: the parsed series list and show pages, written to
/// <c>reshet13-cache.json</c> in the plugin's data folder so a restart does not refetch them.</summary>
internal sealed class Reshet13BrowseCache
{
    public Reshet13BrowseCache(TimedCache value)
    {
        Value = value;
    }

    public TimedCache Value { get; }
}

/// <summary>Reshet 13's resolved-stream-link cache, built with no persistence path so a stream URL
/// never reaches disk (N3) — never give this type's registration a path.</summary>
internal sealed class Reshet13StreamLinkCache
{
    public Reshet13StreamLinkCache(TimedCache value)
    {
        Value = value;
    }

    public TimedCache Value { get; }
}
