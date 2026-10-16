namespace Jellyfin.Plugin.IlVod.Providers.Kan.Source;

/// <summary>One program as a genre page lists it.</summary>
/// <param name="Href">The program's page on kan.org.il, which tells which section it is in.</param>
/// <param name="ImageUrl">Its 16:9 image, absolute and without a size; <c>null</c> when it has none.</param>
public sealed record KanGenreCard(string ProgramId, string Name, string Href, string? ImageUrl);
