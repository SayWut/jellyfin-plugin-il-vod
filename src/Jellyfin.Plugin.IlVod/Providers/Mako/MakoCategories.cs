using Jellyfin.Plugin.IlVod.Core;

namespace Jellyfin.Plugin.IlVod.Providers.Mako;

/// <summary>mako's five real genres, as its genre menu titles them, in the shared vocabulary —
/// which was itself taken from them, so each maps to its own name.</summary>
public static class MakoCategories
{
    public static IReadOnlyDictionary<string, string> Default { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ריאליטי"] = SharedCategories.Reality,
        ["דוקומנטרי"] = SharedCategories.Documentary,
        ["דרמה"] = SharedCategories.Drama,
        ["קומדיה"] = SharedCategories.Comedy,
        ["בישול"] = SharedCategories.Cooking,
    };
}
