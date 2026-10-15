using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.IlVod.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    private const int DefaultStreamLinkCacheMinutes = 5;

    private string[] _disabledProviderIds = [];
    private CategoryOverride[] _categoryOverrides = [];
    private MakoSettings _mako = new();

    /// <summary>
    /// The providers whose content is not loaded at all. Disabled rather than enabled ids, so a
    /// provider added by a plugin update is on until a user turns it off. Never null: a settings
    /// page or config file that omits it means "none disabled".
    /// </summary>
    public string[] DisabledProviderIds
    {
        get => _disabledProviderIds;
        set => _disabledProviderIds = value ?? [];
    }

    /// <summary>The settings page's category table: overrides of each provider's default category mapping.</summary>
    public CategoryOverride[] CategoryOverrides
    {
        get => _categoryOverrides;
        set => _categoryOverrides = value ?? [];
    }

    public MakoSettings Mako
    {
        get => _mako;
        set => _mako = value ?? new MakoSettings();
    }

    /// <summary>
    /// How long a resolved stream link may be reused before play must resolve it again, for every
    /// provider (spec §5.3). N3: the link this governs is never written to disk regardless of this
    /// setting.
    /// </summary>
    public int StreamLinkCacheMinutes { get; set; } = DefaultStreamLinkCacheMinutes;
}
