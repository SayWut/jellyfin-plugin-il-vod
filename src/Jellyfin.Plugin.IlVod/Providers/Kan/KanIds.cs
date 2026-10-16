namespace Jellyfin.Plugin.IlVod.Providers.Kan;

/// <summary>What every Kan id is — a program's, an episode page's — wherever one is checked.</summary>
public static class KanIds
{
    public static bool IsNumeric(string id) => id.Length > 0 && id.All(char.IsAsciiDigit);
}
