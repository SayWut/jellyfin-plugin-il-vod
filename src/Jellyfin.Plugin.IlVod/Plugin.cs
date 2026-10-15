using Jellyfin.Plugin.IlVod.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.IlVod;

public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public const string SettingsPageResource = "Jellyfin.Plugin.IlVod.Configuration.configPage.html";

    public static readonly Guid PluginGuid = Guid.Parse("b8239c51-1963-42d7-82ee-d98ce3e75db4");

    private static Plugin? _instance;

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        _instance = this;
    }

    public static Plugin Instance
        => _instance ?? throw new InvalidOperationException("The IL-VOD plugin has not been loaded yet.");

    public override string Name => "IL-VOD";

    public override Guid Id => PluginGuid;

    public override string Description => "Israeli VOD providers as one Jellyfin channel, fetched on demand.";

    public IEnumerable<PluginPageInfo> GetPages() =>
    [
        new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = SettingsPageResource,
        },
    ];
}
