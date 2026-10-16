namespace Jellyfin.Plugin.IlVod.Providers.Kan.Source;

/// <summary>One of the lobby's genre rails: the genre's name as Kan shows it, and the slug of its
/// genre page, percent-encoded as the lobby links to it.</summary>
public sealed record KanRail(string Name, string Slug);
