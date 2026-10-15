namespace Jellyfin.Plugin.IlVod.MakoApi;

/// <summary>The page paths of mako's show index (spec §6.0): the genre menu, and the rule that
/// tells a real genre's list from the provider, A–B and playlist entries the menu also carries.</summary>
public static class MakoIndexPaths
{
    public const string GenreMenu = "/mako-vod-index?filter=menu";

    private const string IndexPath = "/mako-vod-index";
    private const string GenreFilter = "filter=genre";

    public static bool IsGenreList(string pagePath)
    {
        int queryStart = pagePath.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0 || pagePath[..queryStart] != IndexPath)
        {
            return false;
        }

        return pagePath[(queryStart + 1)..].Split('&').Contains(GenreFilter, StringComparer.Ordinal);
    }
}
