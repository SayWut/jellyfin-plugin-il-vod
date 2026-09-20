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
        // The URL is read through a delegate so a change on the settings page
        // takes effect on the next play, with no restart.
        serviceCollection.AddSingleton<ICatalog>(
            _ => new StaticCatalog(() => Plugin.Instance.Configuration.TestStreamUrl));
        serviceCollection.AddSingleton<IChannel, MakoChannel>();
    }
}
