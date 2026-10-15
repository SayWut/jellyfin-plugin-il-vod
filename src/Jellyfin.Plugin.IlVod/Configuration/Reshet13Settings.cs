namespace Jellyfin.Plugin.IlVod.Configuration;

/// <summary>Reshet 13's own settings: how long each kind of catalogue answer may be served from
/// cache, as whole numbers so the settings page never has to serialise a <see cref="TimeSpan"/>.</summary>
public sealed class Reshet13Settings
{
    private const int DefaultCatalogueCacheHours = 24;
    private const int DefaultShowCacheHours = 6;

    /// <summary>How long the series list may be served from cache.</summary>
    public int CatalogueCacheHours { get; set; } = DefaultCatalogueCacheHours;

    /// <summary>How long a show's episode list (and so its seasons) may be served from cache.</summary>
    public int ShowCacheHours { get; set; } = DefaultShowCacheHours;
}
