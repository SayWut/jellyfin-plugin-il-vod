using Jellyfin.Plugin.Mako.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Mako;

public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public const string SettingsPageResource = "Jellyfin.Plugin.Mako.Configuration.configPage.html";

    public static readonly Guid PluginGuid = Guid.Parse("1b35ed7d-7558-4592-aa1a-681b9d67d305");

    private static Plugin? _instance;

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        _instance = this;
    }

    public static Plugin Instance
        => _instance ?? throw new InvalidOperationException("The Mako plugin has not been loaded yet.");

    public override string Name => "Mako";

    public override Guid Id => PluginGuid;

    public override string Description => "mako 12+ VOD as a Jellyfin channel, fetched on demand.";

    public IEnumerable<PluginPageInfo> GetPages() =>
    [
        new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = SettingsPageResource,
        },
    ];
}
