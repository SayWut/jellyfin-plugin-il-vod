using Jellyfin.Plugin.Mako.Catalog;
using Jellyfin.Plugin.Mako.Configuration;
using Jellyfin.Plugin.Mako.MakoApi;
using MediaBrowser.Controller.Channels;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Mako.Tests;

/// <summary>
/// <c>AddMakoServices</c> is extracted out of <see cref="PluginServiceRegistrator"/> precisely so
/// it can be exercised here, over a temporary data folder and a throwaway configuration, without
/// <c>Plugin.Instance</c> existing — which it does not at real registration time. These tests are
/// N1/N2/N3's proof at the wiring level: the graph resolves to the real live types, the browse
/// cache persists under the data folder, and the stream-link cache persists nowhere at all.
/// </summary>
public sealed class PluginServiceRegistratorTests
{
    [Fact]
    public void TheRegistratorBuildsAWorkingCatalogueGraph()
    {
        using TemporaryDirectory dataFolder = new();
        using ServiceProvider provider = BuildProvider(dataFolder.Path);

        Assert.IsType<MakoCatalog>(provider.GetRequiredService<ICatalog>());
        Assert.IsType<MakoChannel>(provider.GetRequiredService<IChannel>());
    }

    /// <summary>A setting of 0 (or a stray negative value) must not cache the catalogue for no
    /// time at all — that would turn every channel open into a cold, six-request one.
    /// <c>CatalogueLifetimesFrom</c> is <c>internal</c> precisely so this can call it directly,
    /// through this test project's <c>InternalsVisibleTo</c> grant, without building the whole
    /// service graph just to read one number back off it.</summary>
    [Fact]
    public void ACatalogueCacheHoursOfZeroFloorsToOneHour()
    {
        PluginConfiguration configuration = new() { CatalogueCacheHours = 0 };

        CatalogueLifetimes lifetimes = MakoServiceCollectionExtensions.CatalogueLifetimesFrom(configuration);

        Assert.Equal(TimeSpan.FromHours(1), lifetimes.Catalogue);
    }

    [Fact]
    public void TheHttpGraphResolvesOverAHandlerThatNeverFollowsRedirectsAutomatically()
    {
        using TemporaryDirectory dataFolder = new();
        using ServiceProvider provider = BuildProvider(dataFolder.Path);

        // MakoHttpClient itself has no public seam to inspect the handler it was built with, so
        // this checks the two halves this file's own wiring is responsible for keeping true
        // together: the graph resolves a real MakoHttpClient, and the one factory method that
        // wiring calls to build its handler turns off auto-redirect.
        Assert.IsType<MakoHttpClient>(provider.GetRequiredService<IMakoHttp>());
        using SocketsHttpHandler handler = MakoHttpHandlerFactory.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);
    }

    [Fact]
    public void TheStreamLinkCacheWritesNoFileAnywhereUnderTheDataFolder()
    {
        using TemporaryDirectory dataFolder = new();
        using ServiceProvider provider = BuildProvider(dataFolder.Path);

        provider.GetRequiredService<StreamLinkCache>().Value.Write("episode-1", "{\"url\":\"https://cdn.test/x\"}", TimeSpan.FromMinutes(5));

        Assert.Empty(Directory.GetFiles(dataFolder.Path));
    }

    [Fact]
    public void TheBrowseCacheIsPersistedUnderTheDataFolder()
    {
        using TemporaryDirectory dataFolder = new();
        using ServiceProvider provider = BuildProvider(dataFolder.Path);

        provider.GetRequiredService<BrowseCache>().Value.Write("page:/mako-vod-keshet/shemesh", "{}", TimeSpan.FromHours(12));

        Assert.True(File.Exists(Path.Combine(dataFolder.Path, "mako-cache.json")));
    }

    /// <summary>
    /// What Jellyfin 12.1 itself does at startup, reproduced exactly: <c>ApplicationHost</c>'s
    /// <c>SetStaticProperties</c> resolves <c>IChannelManager</c>, whose <c>ChannelManager</c>
    /// constructor materialises <c>IEnumerable&lt;IChannel&gt;</c> with <c>.ToArray()</c> — and
    /// all of that runs BEFORE <c>FindParts</c> calls <c>CreatePlugins</c>, so
    /// <c>Plugin.Instance</c> does not exist yet and both delegates below throw exactly as the
    /// real ones would. An exception escaping here does not disable the plugin; it stops the
    /// server from starting at all. Constructing the channel must therefore touch nothing.
    /// </summary>
    [Fact]
    public void ConstructingTheChannelBeforeThePluginInstanceExistsDoesNotThrow()
    {
        using ServiceProvider provider = BuildProviderWithoutAPluginInstance();

        IChannel[] channels = provider.GetServices<IChannel>().ToArray();

        Assert.Single(channels.OfType<MakoChannel>());
    }

    /// <summary>
    /// Jellyfin's daily <c>RefreshChannels</c> task reads these off every channel it constructed,
    /// and its channel-entity refresh can run before a plugin's own configuration is reachable —
    /// so none of them may reach into the graph either.
    /// </summary>
    [Fact]
    public void TheChannelsOwnDescriptionIsReadableBeforeThePluginInstanceExists()
    {
        using ServiceProvider provider = BuildProviderWithoutAPluginInstance();
        MakoChannel channel = provider.GetServices<IChannel>().OfType<MakoChannel>().Single();

        Assert.Equal("mako", channel.Name);
        Assert.NotEmpty(channel.Description);
        Assert.NotEmpty(channel.DataVersion);
        Assert.NotNull(channel.GetChannelFeatures());
        Assert.Empty(channel.GetSupportedChannelImages());
        Assert.True(channel.IsEnabledFor("x"));
    }

    /// <summary>
    /// The other half of the same rule: deferring construction must not mean never constructing.
    /// Resolving the catalogue is the first user action that builds the whole graph — and only
    /// then is the data folder read. Resolution-only (no <c>GetChannelItems</c> call): the live
    /// <c>MakoCatalog</c> now reaches mako on every browse, so exercising it here would send a
    /// real HTTP request. Resolving <see cref="ICatalog"/> itself is "the first user action" for
    /// this test's purpose — it is exactly what <see cref="MakoChannel"/>'s own
    /// <c>Lazy&lt;ICatalog&gt;</c> forces open on a real first action, and it alone is enough to
    /// prove the data folder is read lazily.
    /// </summary>
    [Fact]
    public void ResolvingTheCatalogueBuildsTheGraphAndReadsTheDataFolder()
    {
        using TemporaryDirectory dataFolder = new();
        int dataFolderReads = 0;
        ServiceCollection services = new();
        services.AddLogging();
        services.AddMakoServices(
            () =>
            {
                dataFolderReads++;
                return dataFolder.Path;
            },
            () => new PluginConfiguration());
        using ServiceProvider provider = services.BuildServiceProvider();
        IChannel channel = provider.GetServices<IChannel>().OfType<MakoChannel>().Single();
        Assert.Equal(0, dataFolderReads);

        Assert.IsType<MakoCatalog>(provider.GetRequiredService<ICatalog>());

        Assert.True(dataFolderReads > 0);
    }

    private static ServiceProvider BuildProvider(string dataFolder)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddMakoServices(() => dataFolder, () => new PluginConfiguration());
        return services.BuildServiceProvider();
    }

    /// <summary>Both delegates fail the way the real ones do before <c>Plugin.Instance</c> is set.</summary>
    private static ServiceProvider BuildProviderWithoutAPluginInstance()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddMakoServices(
            dataFolder: () => throw new InvalidOperationException("The Mako plugin has not been loaded yet."),
            configuration: () => throw new InvalidOperationException("The Mako plugin has not been loaded yet."));
        return services.BuildServiceProvider();
    }

    /// <summary>A fresh, empty directory deleted on dispose, standing in for
    /// <c>Plugin.Instance.DataFolderPath</c>.</summary>
    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mako-registrator-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
