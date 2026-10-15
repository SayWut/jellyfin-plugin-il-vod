namespace Jellyfin.Plugin.IlVod.Model;

public sealed record Episode(
    string Id,
    string ShowName,
    int SeasonNumber,
    int Number,
    string Name,
    DateTime? AirDate,
    TimeSpan? Duration,
    string? ThumbnailUrl);
