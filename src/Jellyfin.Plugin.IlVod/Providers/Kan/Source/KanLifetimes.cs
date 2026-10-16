namespace Jellyfin.Plugin.IlVod.Providers.Kan.Source;

/// <summary>How long Kan's catalogue and a show's feed may be served from cache before a user action
/// is allowed to fetch them again. A stream link's lifetime is not here: it is cached above every
/// provider, by <c>SourceCatalog</c>.</summary>
public sealed record KanLifetimes(TimeSpan Catalogue, TimeSpan Show);
