namespace Jellyfin.Plugin.Mako.Model;

public sealed record Episode(
    string Id,
    string ShowName,
    int SeasonNumber,
    int Number,
    string Name,
    DateTime? AirDate,
    TimeSpan? Duration);
