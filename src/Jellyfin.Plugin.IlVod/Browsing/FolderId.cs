namespace Jellyfin.Plugin.IlVod.Browsing;

public enum FolderKind
{
    Root,
    Show,
    Season,
}

/// <summary>
/// The id the plugin gives a folder item, which Jellyfin hands back when a user opens that folder.
/// </summary>
public sealed record FolderId(FolderKind Kind, string Value)
{
    private const char Separator = '|';
    // Jellyfin derives a channel item's identity from this id and stores the item's type with it.
    // Prefixes changed twice: once when shows became Series items (step A), once in step C when
    // every show had to be created afresh so Jellyfin would copy its genres — it copies them
    // only when it first creates an item.
    private const string ShowPrefix = "mako-series";
    private const string SeasonPrefix = "mako-season";
    private const int SeasonValueParts = 2;

    public static FolderId Root { get; } = new(FolderKind.Root, string.Empty);

    public static FolderId ForShow(string showId) => new(FolderKind.Show, showId);

    public static FolderId ForSeason(string showId, string seasonId) => new(FolderKind.Season, $"{showId}{Separator}{seasonId}");

    public static FolderId Parse(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return Root;
        }

        int separatorAt = raw.IndexOf(Separator, StringComparison.Ordinal);
        if (separatorAt <= 0)
        {
            throw new FormatException($"'{raw}' is not a mako folder id.");
        }

        string prefix = raw[..separatorAt];
        string value = raw[(separatorAt + 1)..];

        return prefix switch
        {
            ShowPrefix => ForShow(value),
            SeasonPrefix => ParseSeason(value, raw),
            _ => throw new FormatException($"'{raw}' is not a mako folder id."),
        };
    }

    public override string ToString() => Kind switch
    {
        FolderKind.Show => $"{ShowPrefix}{Separator}{Value}",
        FolderKind.Season => $"{SeasonPrefix}{Separator}{Value}",
        FolderKind.Root => string.Empty,
        _ => throw new InvalidOperationException($"Folder kind '{Kind}' has no id format."),
    };

    /// <summary>The show path and season path a season id carries; mako's season page names no show,
    /// so the id is the only place the show can come from.</summary>
    public (string ShowId, string SeasonId) SeasonParts()
    {
        if (Kind != FolderKind.Season)
        {
            throw new InvalidOperationException($"A {Kind} folder id has no season parts.");
        }

        string[] parts = Value.Split(Separator);
        return (parts[0], parts[1]);
    }

    private static FolderId ParseSeason(string value, string raw)
    {
        string[] parts = value.Split(Separator);
        if (parts.Length != SeasonValueParts || parts.Any(string.IsNullOrEmpty))
        {
            throw new FormatException($"'{raw}' is not a mako folder id.");
        }

        return new FolderId(FolderKind.Season, value);
    }
}
