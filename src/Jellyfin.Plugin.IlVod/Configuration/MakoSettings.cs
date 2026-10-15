namespace Jellyfin.Plugin.IlVod.Configuration;

/// <summary>mako's own settings: what it reports itself as, and how long each kind of mako page may
/// be served from cache (spec §5.3, §6.3), as whole numbers so the settings page never has to
/// serialise a <see cref="TimeSpan"/>.</summary>
public sealed class MakoSettings
{
    private const string DefaultAppVersion = "43.0";

    private const int DefaultCatalogueCacheHours = 24;
    private const int DefaultShowPageCacheHours = 12;
    private const int DefaultPastSeasonCacheDays = 7;
    private const int DefaultCurrentSeasonCacheHours = 6;

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
}
