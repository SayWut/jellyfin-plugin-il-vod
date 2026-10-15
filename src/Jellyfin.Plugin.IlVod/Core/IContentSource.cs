namespace Jellyfin.Plugin.IlVod.Core;

/// <summary>
/// The fetching half of a provider: asks the provider for its content and answers in the
/// provider's own native records. Owns everything about the network — pacing, caching the raw
/// pages, serving a stale copy when refused — and nothing about the shared model, which is the
/// <see cref="IContentTransformer{TIndex,TShowPage,TSeasonPage,TStream}"/>'s job. The two never
/// reference each other; only <see cref="SourceCatalog{TIndex,TShowPage,TSeasonPage,TStream}"/>
/// joins them.
/// </summary>
/// <typeparam name="TIndex">What the provider lists its shows as.</typeparam>
/// <typeparam name="TShowPage">What the provider answers for one show.</typeparam>
/// <typeparam name="TSeasonPage">What the provider answers for one season.</typeparam>
/// <typeparam name="TStream">What the provider answers when an episode is played.</typeparam>
public interface IContentSource<TIndex, TShowPage, TSeasonPage, TStream>
{
    Task<TIndex> FetchIndexAsync(CancellationToken cancellationToken);

    Task<TShowPage> FetchShowAsync(string showId, CancellationToken cancellationToken);

    Task<TSeasonPage> FetchSeasonAsync(string showId, string seasonId, CancellationToken cancellationToken);

    Task<TStream> FetchStreamAsync(string episodeId, CancellationToken cancellationToken);
}
