namespace Jellyfin.Plugin.IlVod.Configuration;

/// <summary>Kan 11's own settings: how long each kind of catalogue answer may be served from cache,
/// as whole numbers so the settings page never has to serialise a <see cref="TimeSpan"/>.</summary>
public sealed class KanSettings
{
    private const int DefaultCatalogueCacheHours = 24;
    private const int DefaultShowCacheHours = 6;

    /// <summary>How long the show list — the lobby and every genre page — may be served from cache.</summary>
    public int CatalogueCacheHours { get; set; } = DefaultCatalogueCacheHours;

    /// <summary>How long a show's episode feed (and so its seasons) may be served from cache.</summary>
    public int ShowCacheHours { get; set; } = DefaultShowCacheHours;
}
