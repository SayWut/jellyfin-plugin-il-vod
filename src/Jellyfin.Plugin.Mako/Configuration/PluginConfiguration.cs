using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Mako.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Step A only: the signed stream URL every test episode plays. It is pasted by hand
    /// because mako's token expires 15 minutes after it is issued.
    /// </summary>
    public string TestStreamUrl { get; set; } = string.Empty;
}
