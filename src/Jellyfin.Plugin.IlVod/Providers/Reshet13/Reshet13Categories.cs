using Jellyfin.Plugin.IlVod.Core;

namespace Jellyfin.Plugin.IlVod.Providers.Reshet13;

/// <summary>Reshet 13's series genres (the <c>Genre</c> tag), as of 2026-10-16, in the shared
/// vocabulary. <c>דיגיטל 13</c> and <c>כאן חינוכית</c> are brands rather than genres; they map to what
/// their shows are (podcasts and studio talk, one children's show).</summary>
public static class Reshet13Categories
{
    public static IReadOnlyDictionary<string, string> Default { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ריאליטי"] = SharedCategories.Reality,
        ["דוקו-ריאליטי"] = SharedCategories.Reality,
        ["דוקו ותחקירים"] = SharedCategories.Documentary,
        ["דרמה"] = SharedCategories.Drama,
        ["קומדיה"] = SharedCategories.Comedy,
        ["תוכניות אוכל"] = SharedCategories.Cooking,
        ["בידור"] = SharedCategories.Entertainment,
        ["שעשועונים"] = SharedCategories.Entertainment,
        ["כאן חינוכית"] = SharedCategories.Entertainment,
        ["תכניות אירוח"] = SharedCategories.TalkShows,
        ["בידור ואירוח"] = SharedCategories.TalkShows,
        ["דיגיטל 13"] = SharedCategories.TalkShows,
        ["תכניות אקטואליה"] = SharedCategories.CurrentAffairs,
        ["חדשות 13"] = SharedCategories.CurrentAffairs,
        ["לייפסטייל"] = SharedCategories.Lifestyle,
    };
}
