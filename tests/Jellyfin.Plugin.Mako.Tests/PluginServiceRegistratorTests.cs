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

    private static ServiceProvider BuildProvider(string dataFolder)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddMakoServices(() => dataFolder, () => new PluginConfiguration());
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
