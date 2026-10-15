namespace Jellyfin.Plugin.IlVod.Providers.Reshet13.Source;

/// <summary>How long Reshet 13's series list and a show's episode list may be served from cache
/// before a user action is allowed to fetch them again. A stream link's lifetime is not here: it is
/// cached above every provider, by <c>SourceCatalog</c>. The defaults are <c>Reshet13Settings</c>'.</summary>
public sealed record Reshet13Lifetimes(TimeSpan Catalogue, TimeSpan Show);
