namespace Jellyfin.Plugin.Mako.Model;

public sealed record Show(
    string Id,
    string Name,
    string? PosterUrl,
    string? Overview,
    IReadOnlyList<string> Genres);
