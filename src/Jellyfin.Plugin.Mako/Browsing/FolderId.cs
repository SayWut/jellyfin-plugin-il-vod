namespace Jellyfin.Plugin.Mako.Browsing;

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
    // The prefixes changed when show and season folders became Series and Season items: an item
    // already stored as a plain folder cannot be reloaded as a Series, so new ids were needed.
    private const string ShowPrefix = "series";
    private const string SeasonPrefix = "series-season";

    public static FolderId Root { get; } = new(FolderKind.Root, string.Empty);

    public static FolderId ForShow(string showId) => new(FolderKind.Show, showId);

    public static FolderId ForSeason(string seasonId) => new(FolderKind.Season, seasonId);

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
            SeasonPrefix => ForSeason(value),
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
}
