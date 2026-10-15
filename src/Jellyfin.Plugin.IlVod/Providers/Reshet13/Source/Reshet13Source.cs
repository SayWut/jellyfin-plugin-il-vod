using System.Collections.Concurrent;
using Jellyfin.Plugin.IlVod.Cache;
using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Hls;
using Jellyfin.Plugin.IlVod.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IlVod.Providers.Reshet13.Source;

/// <summary>
/// Reshet 13's fetching half: everything it reads, fetched only because a user opened the channel,
/// a show or a season, or pressed play — never ahead of that (N1); <c>Reshet13RequestBudgetTests</c>
/// proves it. Answers in Reshet 13's own records; the transformer turns them into the shared model.
/// <paramref name="readLifetimes"/> is a delegate, not a value, so a change on the settings page
/// takes effect on the next call with no restart.
/// </summary>
public sealed class Reshet13Source : IContentSource<Reshet13ShowIndex, Reshet13ShowPage, Reshet13SeasonPage, Reshet13Stream>
{
    private const string IndexCacheKey = "index";
    private const string ShowCacheKeyPrefix = "show:";
    private const string UnknownShowName = "";

    /// <summary>Reshet 13 always has series: a list naming none is a failure, never an answer —
    /// Jellyfin would delete every show it holds.</summary>
    private const string EmptyCatalogueMessage = "Reshet 13's series list names no series";

    private readonly KalturaOttClient _kaltura;
    private readonly IReshet13Http _http;
    private readonly Reshet13RecordCache _records;
    private readonly Func<Reshet13Lifetimes> _readLifetimes;
    private readonly ILogger<Reshet13Source> _logger;

    /// <summary>
    /// Every episode of every show page this process has produced or read from cache, by asset id,
    /// so pressing play on an episode of an opened show needs no <c>asset/get</c>. Memory only: it
    /// is rebuilt from the cached show pages as shows are opened.
    /// </summary>
    private readonly ConcurrentDictionary<string, Reshet13Episode> _openedEpisodes = new();

    /// <param name="cache">Persisted: it holds only parsed catalogue records, nothing signed.</param>
    public Reshet13Source(
        KalturaOttClient kaltura,
        IReshet13Http http,
        TimedCache cache,
        Func<Reshet13Lifetimes> readLifetimes,
        ILogger<Reshet13Source> logger)
    {
        _kaltura = kaltura;
        _http = http;
        _records = new Reshet13RecordCache(cache);
        _readLifetimes = readLifetimes;
        _logger = logger;
    }

    public Task<Reshet13ShowIndex> FetchIndexAsync(CancellationToken cancellationToken) =>
        FetchThroughCacheAsync(IndexCacheKey, _readLifetimes().Catalogue, () => FetchIndexFromKalturaAsync(cancellationToken));

    /// <summary>
    /// Every episode of the series plus its season assets' names: two requests for most shows. The
    /// episodes are the only way to learn which seasons exist — season assets are incomplete.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="showId"/> is not a Kaltura id — wrong for
    /// good, so it is refused before any request.</exception>
    public async Task<Reshet13ShowPage> FetchShowAsync(string showId, CancellationToken cancellationToken)
    {
        if (!KalturaIds.IsWellFormed(showId))
        {
            throw new ArgumentException($"reshet13 show id '{showId}' is not a series id", nameof(showId));
        }

        Reshet13ShowPage showPage = await FetchThroughCacheAsync(
            ShowCacheKeyPrefix + showId,
            _readLifetimes().Show,
            () => FetchShowFromKalturaAsync(showId, cancellationToken)).ConfigureAwait(false);

        RememberEpisodes(showPage);
        return showPage;
    }

    /// <summary>
    /// The season's episodes, filtered from the show page — so a season of a freshly opened show
    /// costs no request. The show's name comes from its episodes, else from the cached series list,
    /// which is only peeked at, never fetched: a season open must not turn into an index fetch (N1).
    /// </summary>
    public async Task<Reshet13SeasonPage> FetchSeasonAsync(string showId, string seasonId, CancellationToken cancellationToken)
    {
        int? seasonIndex = Reshet13SeasonId.Parse(seasonId);
        Reshet13ShowPage showPage = await FetchShowAsync(showId, cancellationToken).ConfigureAwait(false);

        return new Reshet13SeasonPage(
            ShowNameOf(showId, showPage),
            seasonIndex,
            showPage.SeasonAssetNameOf(seasonIndex),
            [.. showPage.Episodes.Where(episode => episode.SeasonIndex == seasonIndex)]);
    }

    /// <summary>
    /// The clean manifest of the episode's video, described from its master playlist. The episode
    /// comes from an opened show when it can, else from one <c>asset/get</c>; one that states no
    /// length costs one more request, to measure it.
    /// </summary>
    /// <exception cref="StreamUnavailableException"><paramref name="episodeId"/> is not an asset id,
    /// not an episode, or has no HLS file to play.</exception>
    public async Task<Reshet13Stream> FetchStreamAsync(string episodeId, CancellationToken cancellationToken)
    {
        if (!KalturaIds.IsWellFormed(episodeId))
        {
            throw new StreamUnavailableException($"reshet13 episode id '{episodeId}' is not an asset id");
        }

        Reshet13Episode episode = _openedEpisodes.TryGetValue(episodeId, out Reshet13Episode? opened)
            ? opened
            : await FetchEpisodeAsync(episodeId, cancellationToken).ConfigureAwait(false);

        string manifestUrl = Reshet13Urls.CleanManifest(
            episode.EntryId ?? throw new StreamUnavailableException($"reshet13 asset {episodeId} has no HLS file"));

        return await DescribeStreamAsync(manifestUrl, episode.Duration, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A fresh cached copy without a request; otherwise fetched and cached for
    /// <paramref name="lifetime"/>. A fetch that fails because Kaltura could not answer falls back
    /// to a stale copy when there is one. Anything thrown is never cached.
    /// </summary>
    private async Task<T> FetchThroughCacheAsync<T>(string cacheKey, TimeSpan lifetime, Func<Task<T>> fetch)
        where T : class
    {
        if (_records.ReadFresh<T>(cacheKey) is T fresh)
        {
            return fresh;
        }

        try
        {
            T fetched = await fetch().ConfigureAwait(false);
            _records.Write(cacheKey, fetched, lifetime);
            return fetched;
        }
        catch (Reshet13UnavailableException exception)
        {
            if (_records.ReadAny<T>(cacheKey) is not T stale)
            {
                throw;
            }

            _logger.LogWarning("reshet13: serving a stale copy of {CacheKey}: {Reason}", cacheKey, exception.Message);
            return stale;
        }
    }

    private async Task<Reshet13ShowIndex> FetchIndexFromKalturaAsync(CancellationToken cancellationToken)
    {
        Reshet13ShowIndex index = KalturaAssets.ParseIndex(
            await _kaltura.ListAllAsync(KalturaQueries.SeriesPage, cancellationToken).ConfigureAwait(false));

        return index.Series.Count > 0 ? index : throw new FormatException(EmptyCatalogueMessage);
    }

    private async Task<Reshet13ShowPage> FetchShowFromKalturaAsync(string seriesId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Reshet13Episode> episodes = KalturaAssets.ParseEpisodes(
            await _kaltura.ListAllAsync(page => KalturaQueries.EpisodesPage(seriesId, page), cancellationToken).ConfigureAwait(false),
            seriesId);
        IReadOnlyDictionary<int, string> seasonAssetNames = KalturaAssets.ParseSeasonNames(
            await _kaltura.ListAllAsync(page => KalturaQueries.SeasonAssetsPage(seriesId, page), cancellationToken).ConfigureAwait(false));

        return new Reshet13ShowPage(seasonAssetNames, episodes);
    }

    private void RememberEpisodes(Reshet13ShowPage showPage)
    {
        foreach (Reshet13Episode episode in showPage.Episodes)
        {
            _openedEpisodes[episode.AssetId] = episode;
        }
    }

    private string ShowNameOf(string showId, Reshet13ShowPage showPage) =>
        showPage.Episodes.Select(episode => episode.SeriesName).FirstOrDefault(name => !string.IsNullOrEmpty(name))
            ?? CachedSeriesNameOf(showId)
            ?? UnknownShowName;

    /// <summary>A peek at whatever series list is cached, fresh or stale — never a fetch.</summary>
    private string? CachedSeriesNameOf(string seriesId) =>
        _records.ReadAny<Reshet13ShowIndex>(IndexCacheKey)?.Series.FirstOrDefault(series => series.SeriesId == seriesId)?.Name;

    /// <summary>
    /// Play is a request Jellyfin answers directly, so an answer that is no episode is reported as
    /// a playback failure rather than escaping as a bare <see cref="FormatException"/>.
    /// </summary>
    private async Task<Reshet13Episode> FetchEpisodeAsync(string assetId, CancellationToken cancellationToken)
    {
        try
        {
            return KalturaAssets.ParseEpisode(
                await _kaltura.CallAsync(KalturaQueries.Asset(assetId), cancellationToken).ConfigureAwait(false));
        }
        catch (FormatException exception)
        {
            throw new StreamUnavailableException($"reshet13 asset {assetId} is not a playable episode", exception);
        }
    }

    /// <summary>
    /// The stream described from its master playlist, and its length: Kaltura's when it states
    /// one, else measured from the best variant — one more request, and only in that case.
    /// </summary>
    private async Task<Reshet13Stream> DescribeStreamAsync(string manifestUrl, TimeSpan? statedDuration, CancellationToken cancellationToken)
    {
        if (await ReadMasterPlaylistAsync(manifestUrl, cancellationToken).ConfigureAwait(false) is not string masterPlaylistText)
        {
            return new Reshet13Stream(manifestUrl, null, statedDuration);
        }

        TimeSpan? duration = statedDuration
            ?? await MeasureDurationAsync(manifestUrl, masterPlaylistText, cancellationToken).ConfigureAwait(false);
        return new Reshet13Stream(manifestUrl, MasterPlaylist.Describe(masterPlaylistText), duration);
    }

    /// <summary>
    /// Best-effort, the same ruling as mako's <c>StreamResolver</c>: the manifest URL is right
    /// whether or not its playlist could be read just now, so a failure leaves the stream
    /// undescribed (no variant) rather than failing playback; the player reads the playlist itself.
    /// </summary>
    private async Task<string?> ReadMasterPlaylistAsync(string manifestUrl, CancellationToken cancellationToken)
    {
        try
        {
            return await _http.GetTextAsync(manifestUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                "reshet13: could not describe the master playlist at {HostAndPath} ({Reason}); using the default stream description",
                SafeUrl.Redacted(manifestUrl),
                exception.GetType().Name);
            return null;
        }
    }

    /// <summary>
    /// Jellyfin treats a source with no run time as a live stream — players join late and cannot
    /// rewind — so an episode Kaltura states no length for is measured the way mako's are: the
    /// best variant playlist lists every segment, and their sum is the length. Best-effort too: a
    /// failure leaves the length unstated rather than failing playback.
    /// </summary>
    private async Task<TimeSpan?> MeasureDurationAsync(string manifestUrl, string masterPlaylistText, CancellationToken cancellationToken)
    {
        if (MasterPlaylist.BestVariantUri(masterPlaylistText) is not string variantUri
            || !Uri.TryCreate(new Uri(manifestUrl), variantUri, out Uri? variantUrl))
        {
            return null;
        }

        try
        {
            return MediaPlaylist.TotalDuration(await _http.GetTextAsync(variantUrl.AbsoluteUri, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                "reshet13: could not measure the length from {HostAndPath} ({Reason}); Jellyfin will treat the stream as live",
                SafeUrl.Redacted(variantUrl.AbsoluteUri),
                exception.GetType().Name);
            return null;
        }
    }
}
