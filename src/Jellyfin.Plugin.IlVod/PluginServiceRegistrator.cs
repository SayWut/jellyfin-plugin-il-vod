using Jellyfin.Plugin.IlVod.Aggregation;
using Jellyfin.Plugin.IlVod.Cache;
using Jellyfin.Plugin.IlVod.Configuration;
using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Providers.Mako;
using Jellyfin.Plugin.IlVod.Transcoding;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IlVod;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Plugin.Instance does not exist yet at registration time — every read of it below is
        // deferred into a factory delegate, inside AddIlVodServices, never done here.
        serviceCollection.AddIlVodServices(
            dataFolder: () => Plugin.Instance.DataFolderPath,
            configuration: () => Plugin.Instance.Configuration);

        // Jellyfin's global -probesize setting buffers some providers' unidentified data PIDs in
        // RAM until the phone aborts playback (see docs/spec.md §4.3). Wrapping the transcode
        // manager lets the plugin rewrite the probe size for its own streams only. No logger
        // exists yet at this point in startup, so whether it succeeded is recorded in
        // ProbeSizeLimiterStatus.IsInstalled for IlVodChannel to report on the next play.
        _ = TranscodeManagerDecoration.Apply(serviceCollection);
    }
}

/// <summary>
/// Builds the channel's dependency graph: every provider, the catalogue over all of them and the
/// channel itself. Kept separate from <see cref="PluginServiceRegistrator"/> itself so a test can
/// call it directly, over a temporary data folder and a throwaway configuration, without
/// <see cref="Plugin.Instance"/> ever having to exist — see <c>PluginServiceRegistratorTests</c>.
/// </summary>
internal static class IlVodServiceCollectionExtensions
{
    private const string ShowSnapshotsFileName = "show-snapshots.json";

    internal static void AddIlVodServices(this IServiceCollection services, Func<string> dataFolder, Func<PluginConfiguration> configuration)
    {
        services.TryAddSingleton<IClock, SystemClock>();

        // One line per provider. Each registers its own graph, one IContentProvider and its
        // ProviderInfo (read by the probe-size limiter without building the provider's graph).
        services.AddMakoProvider(dataFolder, configuration);

        services.AddSingleton(provider => new ShowListSnapshots(
            new TimedCache(provider.GetRequiredService<IClock>(), PluginDataFolder.PathOf(dataFolder, ShowSnapshotsFileName))));

        services.AddSingleton<ICatalog>(provider => new AggregateCatalog(
            provider.GetServices<IContentProvider>(),
            () => configuration().DisabledProviderIds,
            new CategoryMapper(() => configuration().CategoryOverrides),
            provider.GetRequiredService<ShowListSnapshots>(),
            provider.GetRequiredService<ILogger<AggregateCatalog>>()));

        // Lazy, not ICatalog: Jellyfin constructs every IChannel before any plugin instance
        // exists (see IlVodChannel's own remarks), and building the catalogue reads the data
        // folder and the configuration — both of which throw until Plugin.Instance is set.
        services.AddSingleton<IChannel>(provider => new IlVodChannel(
            new Lazy<ICatalog>(provider.GetRequiredService<ICatalog>),
            provider.GetRequiredService<ILogger<IlVodChannel>>()));
    }
}

/// <summary>Where the plugin keeps its files: the plugin's data folder, shared by every provider —
/// each names its own files.</summary>
internal static class PluginDataFolder
{
    internal static string PathOf(Func<string> dataFolder, string fileName) => Path.Combine(Ensured(dataFolder), fileName);

    /// <summary>
    /// Creates the plugin's data folder if it does not already exist yet, tolerant of failure —
    /// exactly like the best-effort persistence <see cref="TimedCache"/> and the providers' own
    /// files already fall back to, a folder that cannot be created must not stop the plugin from
    /// loading.
    /// </summary>
    private static string Ensured(Func<string> dataFolder)
    {
        string path = dataFolder();
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best-effort only: TimedCache.Write and the providers' own files already tolerate an
            // unwritable location themselves.
        }

        return path;
    }
}
