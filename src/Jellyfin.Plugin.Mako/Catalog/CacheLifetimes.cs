namespace Jellyfin.Plugin.Mako.Catalog;

/// <summary>How long each kind of mako page or stream link may be served from cache before a user
/// action is allowed to refetch it (spec §2).</summary>
public sealed record CacheLifetimes(TimeSpan ShowPage, TimeSpan PastSeason, TimeSpan CurrentSeason, TimeSpan StreamLink)
{
    public static CacheLifetimes Default { get; } = new(
        ShowPage: TimeSpan.FromHours(12),
        PastSeason: TimeSpan.FromDays(7),
        CurrentSeason: TimeSpan.FromHours(6),
        StreamLink: TimeSpan.FromMinutes(5));
}
