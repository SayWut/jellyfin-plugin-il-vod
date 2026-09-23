using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Mako.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    private const string DefaultAppVersion = "43.0";

    private const int DefaultCatalogueCacheHours = 24;
    private const int DefaultShowPageCacheHours = 12;
    private const int DefaultPastSeasonCacheDays = 7;
    private const int DefaultCurrentSeasonCacheHours = 6;
    private const int DefaultStreamLinkCacheMinutes = 5;

    /// <summary>The app version reported to mako in its config and token request URLs, as mako's own Android app would.</summary>
    public string AppVersion { get; set; } = DefaultAppVersion;

    /// <summary>How long the genre menu and each genre's list may be served from cache (spec §6.3).</summary>
    public int CatalogueCacheHours { get; set; } = DefaultCatalogueCacheHours;

    /// <summary>How long a show's page (season list, overview) may be served from cache (spec §5.3).</summary>
    public int ShowPageCacheHours { get; set; } = DefaultShowPageCacheHours;

    /// <summary>How long a past season's episode list may be served from cache (spec §5.3).</summary>
    public int PastSeasonCacheDays { get; set; } = DefaultPastSeasonCacheDays;

    /// <summary>How long the current season's episode list may be served from cache (spec §5.3).</summary>
    public int CurrentSeasonCacheHours { get; set; } = DefaultCurrentSeasonCacheHours;

    /// <summary>
    /// How long a resolved stream link may be reused before play must resolve it again (spec
    /// §5.3). N3: the link this governs is never written to disk regardless of this setting.
    /// </summary>
    public int StreamLinkCacheMinutes { get; set; } = DefaultStreamLinkCacheMinutes;
}
