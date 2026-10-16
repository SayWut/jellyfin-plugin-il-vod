using Jellyfin.Plugin.IlVod.Core;

namespace Jellyfin.Plugin.IlVod.Providers.Kan;

/// <summary>
/// The Kan BOX genres (its lobby's rails), as of 2026-10-16, in the shared vocabulary. סרטים are
/// almost all documentary films; מוזיקה is concerts, the chart show and music competitions. The
/// archive, kids, web-series and sport rails list no Kan 11 show today but are mapped too, so a show
/// Kan files there later still lands somewhere sensible.
/// </summary>
public static class KanCategories
{
    public static IReadOnlyDictionary<string, string> Default { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["דוקו"] = SharedCategories.Documentary,
        ["סרטים"] = SharedCategories.Documentary,
        ["בחזרה לעבר"] = SharedCategories.Documentary,
        ["יהדות ומורשת"] = SharedCategories.Documentary,
        ["דוקו מהארכיון"] = SharedCategories.Documentary,
        ["אוצרות הארכיון"] = SharedCategories.Documentary,
        ["דרמה"] = SharedCategories.Drama,
        ["דרמה מהארכיון"] = SharedCategories.Drama,
        ["לא מכאן: סדרות חו\"ל"] = SharedCategories.Drama,
        ["פשע"] = SharedCategories.Drama,
        ["קומדיה וסאטירה"] = SharedCategories.Comedy,
        ["קומדיה וסאטירה מהארכיון"] = SharedCategories.Comedy,
        ["דוקו ריאליטי"] = SharedCategories.Reality,
        ["בידור"] = SharedCategories.Entertainment,
        ["בידור ואירוח מהארכיון"] = SharedCategories.Entertainment,
        ["מוזיקה"] = SharedCategories.Entertainment,
        ["מפעילים את המוח"] = SharedCategories.Entertainment,
        ["סדרות רשת"] = SharedCategories.Entertainment,
        ["ילדים ונוער"] = SharedCategories.Entertainment,
        ["ילדים ונוער מהארכיון"] = SharedCategories.Entertainment,
        ["הליגה הלאומית"] = SharedCategories.Entertainment,
        ["תרבות ופנאי"] = SharedCategories.Lifestyle,
        ["תרבות ופנאי מהארכיון"] = SharedCategories.Lifestyle,
        ["תוכניות אקטואליה"] = SharedCategories.CurrentAffairs,
    };
}
