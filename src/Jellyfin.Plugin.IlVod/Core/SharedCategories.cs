namespace Jellyfin.Plugin.IlVod.Core;

/// <summary>
/// The categories the channel shows, whichever provider a show comes from: one closed set, so
/// adding a provider never adds categories of its own. A provider's category names map onto these
/// (<see cref="ProviderInfo.DefaultCategories"/>, overridable on the settings page); a show whose
/// names map to none is listed under <see cref="Fallback"/>.
/// </summary>
public static class SharedCategories
{
    public const string Reality = "ריאליטי";
    public const string Documentary = "דוקומנטרי";
    public const string Drama = "דרמה";
    public const string Comedy = "קומדיה";
    public const string Cooking = "בישול";
    public const string Entertainment = "בידור";
    public const string TalkShows = "אירוח";
    public const string CurrentAffairs = "אקטואליה";
    public const string Lifestyle = "לייפסטייל";

    /// <summary>Where a show goes when no mapping names any of its categories, or it has none.</summary>
    public const string Fallback = "אחר";

    public static IReadOnlyList<string> All { get; } =
        [Reality, Documentary, Drama, Comedy, Cooking, Entertainment, TalkShows, CurrentAffairs, Lifestyle, Fallback];
}
