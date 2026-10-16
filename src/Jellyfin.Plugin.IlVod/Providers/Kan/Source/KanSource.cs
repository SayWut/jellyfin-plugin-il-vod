using System.Collections.Concurrent;
using Jellyfin.Plugin.IlVod.Cache;
using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Hls;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IlVod.Providers.Kan.Source;

/// <summary>
/// Kan 11's fetching half: everything it reads, fetched only because a user opened the channel, a
/// show or a season, or pressed play — never ahead of that (N1). Answers in Kan's own records; the
/// transformer turns them into the shared model. <paramref name="readLifetimes"/> is a delegate, not
/// a value, so a change on the settings page takes effect on the next call with no restart.
/// </summary>
public sealed class KanSource : IContentSource<KanShowIndex, KanShowPage, KanSeasonPage, KanStream>
{
    private const string IndexCacheKey = "index";
    private const string ShowCacheKeyPrefix = "show:";

    /// <summary>Rails are walked a few at a time — a cold catalogue is some 40 pages, and Kan is
    /// asked politely — while one rail's pages are read in order.</summary>
    private const int RailsWalkedAtOnce = 4;

    /// <summary>The largest genre is a handful of pages; a page claiming far more is not believed,
    /// so a broken pager can never turn one open into thousands of requests.</summary>
    private const int MostGenrePages = 50;

    private readonly IKanHttp _http;
    private readonly RecordCache _records;
    private readonly Func<KanLifetimes> _readLifetimes;
    private readonly ILogger<KanSource> _logger;
    private readonly HlsPlaylistReader _playlists;

    /// <summary>
    /// Every episode of every show page this process has produced or read from cache, by episode
    /// id, so pressing play on an episode of an opened show knows its length without measuring it.
    /// Memory only: it is rebuilt from the cached show pages as shows are opened.
    /// </summary>
    private readonly ConcurrentDictionary<string, KanEntry> _openedEpisodes = new();

    /// <param name="cache">Persisted: it holds only parsed catalogue records, nothing signed.</param>
    public KanSource(IKanHttp http, TimedCache cache, Func<KanLifetimes> readLifetimes, ILogger<KanSource> logger)
    {
        _http = http;
        _records = new RecordCache(cache);
        _readLifetimes = readLifetimes;
        _logger = logger;
        _playlists = new HlsPlaylistReader(http.GetTextAsync, KanProvider.Description.Id, logger);
    }

    public Task<KanShowIndex> FetchIndexAsync(CancellationToken cancellationToken) =>
        FetchThroughCacheAsync(IndexCacheKey, _readLifetimes().Catalogue, () => FetchIndexFromKanAsync(cancellationToken));

    /// <summary>One request: the program's feed lists every episode with what the show needs.</summary>
    /// <exception cref="ArgumentException"><paramref name="showId"/> is not a program id — wrong for
    /// good, so it is refused before any request.</exception>
    public async Task<KanShowPage> FetchShowAsync(string showId, CancellationToken cancellationToken)
    {
        if (!KanIds.IsNumeric(showId))
        {
            throw new ArgumentException($"kan show id '{showId}' is not a program id", nameof(showId));
        }

        KanShowPage showPage = await FetchThroughCacheAsync(
            ShowCacheKeyPrefix + showId,
            _readLifetimes().Show,
            async () => KanFeed.Parse(await _http.GetTextAsync(KanUrls.ProgramFeed(showId), cancellationToken).ConfigureAwait(false), showId))
            .ConfigureAwait(false);

        RememberEpisodes(showPage);
        return showPage;
    }

    /// <summary>The season's episodes, filtered from the show page — so a season of a freshly
    /// opened show costs no request, and never turns into an index fetch (N1).</summary>
    public async Task<KanSeasonPage> FetchSeasonAsync(string showId, string seasonId, CancellationToken cancellationToken)
    {
        int? season = KanSeasonId.Parse(seasonId);
        KanShowPage showPage = await FetchShowAsync(showId, cancellationToken).ConfigureAwait(false);

        return new KanSeasonPage(
            showPage.ProgramId,
            showPage.ShowName,
            season,
            showPage.HasNumberedSeasons(),
            [.. showPage.Episodes.Where(episode => episode.Season == season)]);
    }

    /// <summary>
    /// The episode's master playlist, read off its page and described: two requests. The master is
    /// what plays, never its best variant — Kan's variants are video only, the audio is a separate
    /// rendition; the stream indices tell ffmpeg which of the master's streams to map. An episode
    /// whose length no feed this process has seen states costs one more request, to measure it.
    /// </summary>
    /// <exception cref="StreamUnavailableException"><paramref name="episodeId"/> is not an episode
    /// id, or its page has no player on Redge's CDN.</exception>
    public async Task<KanStream> FetchStreamAsync(string episodeId, CancellationToken cancellationToken)
    {
        if (!KanEpisodeId.TryParse(episodeId, out string? programId, out string? entryId))
        {
            throw new StreamUnavailableException($"kan episode id '{episodeId}' is not an episode id");
        }

        string masterUrl = await FetchMasterUrlAsync(programId, entryId, cancellationToken).ConfigureAwait(false);
        return await DescribeStreamAsync(masterUrl, KnownLengthOf(episodeId, programId, entryId), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A fresh cached copy without a request; otherwise fetched and cached for
    /// <paramref name="lifetime"/>. A fetch that fails because Kan could not answer falls back to a
    /// stale copy when there is one. Anything thrown is never cached.
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
        catch (KanUnavailableException exception)
        {
            if (_records.ReadAny<T>(cacheKey) is not T stale)
            {
                throw;
            }

            _logger.LogWarning("kan: serving a stale copy of {CacheKey}: {Reason}", cacheKey, exception.Message);
            return stale;
        }
    }

    /// <summary>The lobby names the genres; only walking every genre's pages lists every show.</summary>
    private async Task<KanShowIndex> FetchIndexFromKanAsync(CancellationToken cancellationToken)
    {
        KanLobby lobby = KanPages.ParseLobby(await _http.GetTextAsync(KanUrls.Lobby, cancellationToken).ConfigureAwait(false));
        var cardsByRail = new IReadOnlyList<KanGenreCard>[lobby.Rails.Count];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, lobby.Rails.Count),
            new ParallelOptions { MaxDegreeOfParallelism = RailsWalkedAtOnce, CancellationToken = cancellationToken },
            async (railIndex, token) => cardsByRail[railIndex] = await WalkRailAsync(lobby.Rails[railIndex], token).ConfigureAwait(false))
            .ConfigureAwait(false);

        return KanShowIndexComposer.Compose(lobby, cardsByRail);
    }

    /// <summary>
    /// Every card of the rail's genre, page by page. Kan's pages are sorted on a key that is not
    /// unique, so a card can repeat on the next page while another slips between them: repeats are
    /// dropped, and a walk that ends short of the genre's stated total is walked once more. Still
    /// short, Kan is answering inconsistently right now — unavailable, so a stale copy may stand in
    /// — and never answered short, or Jellyfin would delete the shows it missed.
    /// </summary>
    /// <exception cref="KanUnavailableException">Both walks ended short.</exception>
    /// <exception cref="FormatException">The pager is not believable.</exception>
    private async Task<IReadOnlyList<KanGenreCard>> WalkRailAsync(KanRail rail, CancellationToken cancellationToken)
    {
        (IReadOnlyList<KanGenreCard> cards, int statedTotal) = await WalkRailOnceAsync(rail, cancellationToken).ConfigureAwait(false);
        if (cards.Count < statedTotal)
        {
            (cards, statedTotal) = await WalkRailOnceAsync(rail, cancellationToken).ConfigureAwait(false);
        }

        return cards.Count >= statedTotal
            ? cards
            : throw new KanUnavailableException($"kan genre '{rail.Slug}' listed {cards.Count} of the {statedTotal} programs it states, twice");
    }

    private async Task<(IReadOnlyList<KanGenreCard> Cards, int StatedTotal)> WalkRailOnceAsync(KanRail rail, CancellationToken cancellationToken)
    {
        KanGenrePage firstPage = await FetchGenrePageAsync(rail, 1, cancellationToken).ConfigureAwait(false);
        if (firstPage.LastPage > MostGenrePages)
        {
            throw new FormatException($"kan genre '{rail.Slug}' claims {firstPage.LastPage} pages");
        }

        List<KanGenreCard> cards = [.. firstPage.Cards];
        for (int page = 2; page <= firstPage.LastPage; page++)
        {
            cards.AddRange((await FetchGenrePageAsync(rail, page, cancellationToken).ConfigureAwait(false)).Cards);
        }

        return ([.. cards.DistinctBy(card => card.ProgramId)], firstPage.StatedTotal);
    }

    private async Task<KanGenrePage> FetchGenrePageAsync(KanRail rail, int page, CancellationToken cancellationToken) =>
        KanPages.ParseGenrePage(await _http.GetTextAsync(KanUrls.GenrePage(rail.Slug, page), cancellationToken).ConfigureAwait(false));

    private void RememberEpisodes(KanShowPage showPage)
    {
        foreach (KanEntry episode in showPage.Episodes)
        {
            _openedEpisodes[KanEpisodeId.Of(showPage.ProgramId, episode.EntryId)] = episode;
        }
    }

    /// <summary>The feed's length for the episode: from a show this process opened, else from a
    /// peek at whatever copy of the show is cached, fresh or stale — never a fetch: play must not
    /// turn into a feed request (N1).</summary>
    private TimeSpan? KnownLengthOf(string episodeId, string programId, string entryId) =>
        _openedEpisodes.TryGetValue(episodeId, out KanEntry? opened)
            ? opened.Duration
            : _records.ReadAny<KanShowPage>(ShowCacheKeyPrefix + programId)
                ?.Episodes.FirstOrDefault(episode => episode.EntryId == entryId)
                ?.Duration;

    /// <summary>
    /// Play is a request Jellyfin answers directly, so a page that has no usable player is reported
    /// as a playback failure rather than escaping as a bare <see cref="FormatException"/>. A page
    /// Kan could not serve is <see cref="KanUnavailableException"/>, as anywhere else.
    /// </summary>
    private async Task<string> FetchMasterUrlAsync(string programId, string entryId, CancellationToken cancellationToken)
    {
        string episodePage = await _http.GetTextAsync(KanUrls.EpisodePage(programId, entryId), cancellationToken).ConfigureAwait(false);
        try
        {
            return KanPlayerConfig.ParseHlsUrl(episodePage, entryId);
        }
        catch (FormatException exception)
        {
            throw new StreamUnavailableException($"kan episode {programId}-{entryId} has no playable player", exception);
        }
    }

    private async Task<KanStream> DescribeStreamAsync(string masterUrl, TimeSpan? knownLength, CancellationToken cancellationToken)
    {
        if (await _playlists.ReadMasterAsync(masterUrl, cancellationToken).ConfigureAwait(false) is not string masterPlaylistText)
        {
            return new KanStream(masterUrl, null, null, knownLength);
        }

        TimeSpan? length = knownLength
            ?? await _playlists.MeasureLengthAsync(HlsPlaylistReader.BestVariantUrlOf(masterUrl, masterPlaylistText), cancellationToken).ConfigureAwait(false);
        return new KanStream(
            masterUrl,
            MasterPlaylist.Describe(masterPlaylistText),
            MasterPlaylist.FfmpegStreamIndices(masterPlaylistText),
            length);
    }
}
