namespace Jellyfin.Plugin.IlVod.Providers.Reshet13.Source;

/// <summary>How long Reshet 13's series list and a show's episode list may be served from cache
/// before a user action is allowed to fetch them again. A stream link's lifetime is not here: it is
/// cached above every provider, by <c>SourceCatalog</c>.</summary>
public sealed record Reshet13Lifetimes(TimeSpan Catalogue, TimeSpan Show)
{
    public static Reshet13Lifetimes Default { get; } = new(
        Catalogue: TimeSpan.FromHours(24),
        Show: TimeSpan.FromHours(6));
}
