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
    private const string ShowPrefix = "show";
    private const string SeasonPrefix = "season";

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
