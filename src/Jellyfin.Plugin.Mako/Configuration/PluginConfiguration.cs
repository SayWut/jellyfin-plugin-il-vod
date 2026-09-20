using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Mako.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Step A only: the signed stream URL every test episode plays. It is pasted by hand
    /// because mako's token expires 15 minutes after it is issued.
    /// </summary>
    public string TestStreamUrl { get; set; } = string.Empty;

    /// <summary>
    /// Step A only: the container every test episode reports to Jellyfin. Empty lets Jellyfin
    /// decide; "hls" forces the manifest path, which Jellyfin 12.1 never plays directly. Step A
    /// check 5 is run once with each.
    /// </summary>
    public string TestStreamContainer { get; set; } = string.Empty;
}
