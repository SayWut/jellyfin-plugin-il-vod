namespace Jellyfin.Plugin.IlVod.Core;

/// <summary>
/// The categories the channel shows, whichever provider a show comes from. A provider's own
/// category names map onto these (<see cref="ProviderInfo.DefaultCategories"/>, overridable on the
/// settings page); a name that maps to none is shown as the provider spells it.
/// </summary>
public static class SharedCategories
{
    public const string Reality = "ריאליטי";
    public const string Documentary = "דוקומנטרי";
    public const string Drama = "דרמה";
    public const string Comedy = "קומדיה";
    public const string Cooking = "בישול";

    public static IReadOnlyList<string> All { get; } = [Reality, Documentary, Drama, Comedy, Cooking];
}
