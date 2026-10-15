namespace Jellyfin.Plugin.IlVod.Providers.Mako;

/// <summary>How long each kind of mako page or stream link may be served from cache before a user
/// action is allowed to refetch it (spec §2).</summary>
public sealed record CatalogueLifetimes(TimeSpan Catalogue, TimeSpan ShowPage, TimeSpan PastSeason, TimeSpan CurrentSeason, TimeSpan StreamLink)
{
    public static CatalogueLifetimes Default { get; } = new(
        Catalogue: TimeSpan.FromHours(24),
        ShowPage: TimeSpan.FromHours(12),
        PastSeason: TimeSpan.FromDays(7),
        CurrentSeason: TimeSpan.FromHours(6),
        StreamLink: TimeSpan.FromMinutes(5));
}
