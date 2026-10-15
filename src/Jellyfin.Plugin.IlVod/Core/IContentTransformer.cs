namespace Jellyfin.Plugin.IlVod.Core;

/// <summary>
/// The converting half of a provider: turns the provider's native records into the shared model.
/// Pure — no network, no cache, no clock — so it can be tested from saved pages alone. Category
/// names are passed through exactly as the provider spells them; mapping them onto the shared
/// categories happens above every provider, in the channel's aggregation.
/// </summary>
public interface IContentTransformer<in TIndex, in TShowPage, in TSeasonPage, in TStream>
{
    IReadOnlyList<Show> ToShows(TIndex index);

    IReadOnlyList<Season> ToSeasons(TShowPage showPage);

    IReadOnlyList<Episode> ToEpisodes(TSeasonPage seasonPage);

    StreamLink ToStreamLink(TStream stream);
}
