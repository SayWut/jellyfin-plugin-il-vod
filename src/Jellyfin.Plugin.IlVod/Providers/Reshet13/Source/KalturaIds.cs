namespace Jellyfin.Plugin.IlVod.Providers.Reshet13.Source;

/// <summary>
/// The shape of every Kaltura id Reshet 13 uses — a series' <c>SeriesID</c> and an asset id are
/// both plain decimal numbers. Ids come back from Jellyfin and end up inside a <c>kSql</c> filter,
/// so nothing else is ever let through.
/// </summary>
public static class KalturaIds
{
    public static bool IsWellFormed(string id) => id.Length > 0 && id.All(char.IsAsciiDigit);
}
