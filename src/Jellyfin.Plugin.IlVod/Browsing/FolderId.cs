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
    // Jellyfin derives a channel item's identity from this id and stores the item's type with it,
    // so changing a prefix makes Jellyfin recreate every folder. Provider-neutral since IL-VOD:
    // the show id a folder carries is already scoped to its provider (ProviderScopedId).
    private const string ShowPrefix = "series";
    private const string SeasonPrefix = "season";
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
            throw new FormatException($"'{raw}' is not an IL-VOD folder id.");
        }

        string prefix = raw[..separatorAt];
        string value = raw[(separatorAt + 1)..];

        return prefix switch
        {
            ShowPrefix => ForShow(value),
            SeasonPrefix => ParseSeason(value, raw),
            _ => throw new FormatException($"'{raw}' is not an IL-VOD folder id."),
        };
    }

    public override string ToString() => Kind switch
    {
        FolderKind.Show => $"{ShowPrefix}{Separator}{Value}",
        FolderKind.Season => $"{SeasonPrefix}{Separator}{Value}",
        FolderKind.Root => string.Empty,
        _ => throw new InvalidOperationException($"Folder kind '{Kind}' has no id format."),
    };

    /// <summary>The show id and season id a season folder carries; a provider's season need not name
    /// its show (mako's season page does not), so the folder id is where the show comes from.</summary>
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
            throw new FormatException($"'{raw}' is not an IL-VOD folder id.");
        }

        return new FolderId(FolderKind.Season, value);
    }
}
