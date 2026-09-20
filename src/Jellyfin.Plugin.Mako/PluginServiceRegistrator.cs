using Jellyfin.Plugin.Mako.Catalog;
using Jellyfin.Plugin.Mako.Transcoding;
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

        // Jellyfin's global -probesize setting buffers mako's unidentified data PID in RAM
        // until the phone aborts playback (see docs/spec.md §4.3). Wrapping the transcode
        // manager lets the plugin rewrite the probe size for its own streams only.
        TranscodeManagerDecoration.Apply(serviceCollection);
    }
}
