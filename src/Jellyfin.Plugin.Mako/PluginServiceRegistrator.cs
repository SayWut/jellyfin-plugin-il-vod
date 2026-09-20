using Jellyfin.Plugin.Mako.Catalog;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Mako;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // The URL and container are read through delegates so a change on the settings page
        // takes effect on the next play, with no restart.
        serviceCollection.AddSingleton<ICatalog>(
            _ => new StaticCatalog(
                () => Plugin.Instance.Configuration.TestStreamUrl,
                () => Plugin.Instance.Configuration.TestStreamContainer));
        serviceCollection.AddSingleton<IChannel, MakoChannel>();
    }
}
