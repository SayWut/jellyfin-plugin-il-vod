using System.Text.Json;
using Jellyfin.Plugin.IlVod.Cache;

namespace Jellyfin.Plugin.IlVod.Core;

/// <summary>
/// A provider's <see cref="ICatalog"/>, joined from its source and its transformer: every answer
/// is fetched by the one and converted by the other. Also the one place a resolved stream link
/// is cached, for every provider alike — in memory only, since a signed URL must never sit on
/// disk (N3); give <paramref name="streamLinks"/> no persistence path.
/// </summary>
public sealed class SourceCatalog<TIndex, TShowPage, TSeasonPage, TStream> : ICatalog
{
    private readonly IContentSource<TIndex, TShowPage, TSeasonPage, TStream> _source;
    private readonly IContentTransformer<TIndex, TShowPage, TSeasonPage, TStream> _transformer;
    private readonly TimedCache _streamLinks;
    private readonly Func<TimeSpan> _readStreamLinkLifetime;

    /// <param name="readStreamLinkLifetime">A delegate, not a value, so a change on the settings
    /// page takes effect on the next play with no restart.</param>
    public SourceCatalog(
        IContentSource<TIndex, TShowPage, TSeasonPage, TStream> source,
        IContentTransformer<TIndex, TShowPage, TSeasonPage, TStream> transformer,
        TimedCache streamLinks,
        Func<TimeSpan> readStreamLinkLifetime)
    {
        _source = source;
        _transformer = transformer;
        _streamLinks = streamLinks;
        _readStreamLinkLifetime = readStreamLinkLifetime;
    }

    public async Task<IReadOnlyList<Show>> GetShowsAsync(CancellationToken cancellationToken) =>
        _transformer.ToShows(await _source.FetchIndexAsync(cancellationToken).ConfigureAwait(false));

    public async Task<IReadOnlyList<Season>> GetSeasonsAsync(string showId, CancellationToken cancellationToken) =>
        _transformer.ToSeasons(await _source.FetchShowAsync(showId, cancellationToken).ConfigureAwait(false));

    public async Task<IReadOnlyList<Episode>> GetEpisodesAsync(string showId, string seasonId, CancellationToken cancellationToken) =>
        _transformer.ToEpisodes(await _source.FetchSeasonAsync(showId, seasonId, cancellationToken).ConfigureAwait(false));

    public async Task<StreamLink> GetStreamLinkAsync(string episodeId, CancellationToken cancellationToken)
    {
        CacheRead cached = _streamLinks.Read(episodeId);
        if (cached.IsFresh)
        {
            return DeserializeStreamLink(cached.Value!);
        }

        TStream stream = await _source.FetchStreamAsync(episodeId, cancellationToken).ConfigureAwait(false);
        StreamLink link = _transformer.ToStreamLink(stream);
        _streamLinks.Write(episodeId, JsonSerializer.Serialize(link), _readStreamLinkLifetime());
        return link;
    }

    private static StreamLink DeserializeStreamLink(string json) =>
        JsonSerializer.Deserialize<StreamLink>(json)
            ?? throw new JsonException("cached stream link deserialized to null");
}
