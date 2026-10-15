using Jellyfin.Plugin.IlVod.Core;

namespace Jellyfin.Plugin.IlVod.Providers.Mako;

/// <summary>mako's category lists, as its genre menu titles them, in the shared vocabulary: its
/// five real genres — from which the first five shared categories were taken, so each maps to its
/// own name — and its news list.</summary>
public static class MakoCategories
{
    public static IReadOnlyDictionary<string, string> Default { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ריאליטי"] = SharedCategories.Reality,
        ["דוקומנטרי"] = SharedCategories.Documentary,
        ["דרמה"] = SharedCategories.Drama,
        ["קומדיה"] = SharedCategories.Comedy,
        ["בישול"] = SharedCategories.Cooking,
        ["החדשות"] = SharedCategories.CurrentAffairs,
    };
}
