namespace Jellyfin.Plugin.IlVod.Providers.Mako.Source;

/// <summary>The page paths of mako's show index (spec §6.0): the full show list, the genre menu,
/// and the rule that tells a category list — a real genre, or an allowlisted provider list — from
/// the other provider, A–B and playlist entries the menu also carries.</summary>
public static class MakoIndexPaths
{
    /// <summary>The menu's "כל התכניות" entry: every show mako lists, about a third of which no
    /// category list names.</summary>
    public const string FullIndex = IndexPath;

    public const string GenreMenu = "/mako-vod-index?filter=menu";

    private const string IndexPath = "/mako-vod-index";
    private const string GenreFilter = "filter=genre";
    private const string ProviderFilter = "filter=provider";

    /// <summary>The provider lists whose names count as a category, by menu title. Only the news:
    /// its shows are in the full list but in no genre, while podcast and ערוץ 24 shows are not in
    /// the full list at all.</summary>
    private static readonly string[] CategorisedProviderLists = ["החדשות"];

    public static bool IsGenreList(string pagePath) => HasIndexFilter(pagePath, GenreFilter);

    public static bool IsCategoryList(string menuTitle, string pagePath) =>
        IsGenreList(pagePath) || IsCategorisedProviderList(menuTitle, pagePath);

    private static bool IsCategorisedProviderList(string menuTitle, string pagePath) =>
        CategorisedProviderLists.Contains(menuTitle, StringComparer.Ordinal) && HasIndexFilter(pagePath, ProviderFilter);

    private static bool HasIndexFilter(string pagePath, string filter)
    {
        int queryStart = pagePath.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0 || pagePath[..queryStart] != IndexPath)
        {
            return false;
        }

        return pagePath[(queryStart + 1)..].Split('&').Contains(filter, StringComparer.Ordinal);
    }
}
