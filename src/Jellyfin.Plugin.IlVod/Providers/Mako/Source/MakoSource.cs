using Jellyfin.Plugin.IlVod.Core;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IlVod.Providers.Mako.Source;

/// <summary>
/// mako's fetching half (spec §6.2): every page it reads, fetched only because a user opened the
/// channel, a show, or pressed play — nothing here ever fetches ahead of that action (N1);
/// <c>OnDemandRequestBudgetTests</c> is the test that proves it. Answers in mako's own records;
/// <c>MakoTransformer</c> turns them into the shared model. <paramref name="readLifetimes"/> is a
/// delegate, not a value, so a change on the settings page takes effect on the next call with no
/// restart.
/// </summary>
public sealed class MakoSource : IContentSource<MakoShowIndex, ShowPage, MakoSeasonPage, MakoResolvedStream>
{
    private const int FallbackSeasonNumber = 1;
    private const string UnknownShowName = "";

    /// <summary>Mako's real genres are never empty (§6.2's controller ruling): a menu naming none
    /// is treated the same as one that is not JSON at all — a failure, never cached.</summary>
    private const string EmptyGenreMenuMessage = "mako's genre menu names no genre";

    /// <summary>Same ruling as <see cref="EmptyGenreMenuMessage"/>, for a genre's own list.</summary>
    private const string EmptyGenreListMessage = "mako's genre list names no show";

    /// <summary>Logged by <see cref="ParseOrEmpty{T}"/>, which only ever runs against an already
    /// cached page — this is a stale/corrupted cache entry, not a live browse failure.</summary>
    private const string CachedIndexPageUnparseableWarning =
        "mako: a cached index page could not be parsed; episodes will carry no series name";

    private readonly MakoPageSource _pages;
    private readonly StreamResolver _resolver;
    private readonly Func<CatalogueLifetimes> _readLifetimes;
    private readonly ILogger<MakoSource> _logger;

    public MakoSource(
        MakoPageSource pages,
        StreamResolver resolver,
        Func<CatalogueLifetimes> readLifetimes,
        ILogger<MakoSource> logger)
    {
        _pages = pages;
        _resolver = resolver;
        _readLifetimes = readLifetimes;
        _logger = logger;
    }

    /// <summary>
    /// The genre menu, then each real genre's list (spec §6.2). The lists are pages like any other,
    /// so <see cref="MakoPageSource"/> caches, coalesces and stale-serves them; a list that fails
    /// with no stale copy fails the whole answer, because a shorter answer would make Jellyfin
    /// delete the missing shows.
    /// </summary>
    public async Task<MakoShowIndex> FetchIndexAsync(CancellationToken cancellationToken)
    {
        TimeSpan lifetime = _readLifetimes().Catalogue;
        string menuJson = await _pages.GetAsync(MakoIndexPaths.GenreMenu, MenuLifetimeOf(lifetime), cancellationToken).ConfigureAwait(false);

        List<MakoGenreList> genres = [];
        foreach (Genre genre in MakoIndex.ParseGenres(menuJson))
        {
            string listJson = await _pages.GetAsync(genre.PagePath, ListLifetimeOf(lifetime), cancellationToken).ConfigureAwait(false);
            genres.Add(new MakoGenreList(genre.Name, MakoIndex.ParseEntries(listJson)));
        }

        return new MakoShowIndex(genres);
    }

    public async Task<ShowPage> FetchShowAsync(string showId, CancellationToken cancellationToken)
    {
        CatalogueLifetimes lifetimes = _readLifetimes();
        string showJson = await _pages.GetAsync(showId, ShowPageLifetimeOf(lifetimes), cancellationToken).ConfigureAwait(false);
        ShowPage showPage = MakoPages.ParseShowPage(showJson);

        RememberDisplayedSeason(showPage, showJson, showId, lifetimes);

        return showPage;
    }

    /// <summary>
    /// The show's name comes from the cached genre lists (<see cref="ShowNameOf"/>), not from a
    /// fetch — a season open must never turn into an index burst (N1).
    /// </summary>
    public async Task<MakoSeasonPage> FetchSeasonAsync(string showId, string seasonId, CancellationToken cancellationToken)
    {
        CatalogueLifetimes lifetimes = _readLifetimes();

        string seasonJson = await _pages
            .GetAsync(seasonId, pageJson => LifetimeOf(pageJson, seasonId, lifetimes), cancellationToken)
            .ConfigureAwait(false);

        return new MakoSeasonPage(ShowNameOf(showId), SeasonNumberOf(seasonJson, seasonId), MakoPages.ParseEpisodes(seasonJson));
    }

    public Task<MakoResolvedStream> FetchStreamAsync(string episodeId, CancellationToken cancellationToken) =>
        _resolver.ResolveAsync(ParseEpisodeIdOrThrow(episodeId), cancellationToken);

    /// <summary>
    /// The page just fetched under <paramref name="fetchedPagePath"/> also carries the episodes of
    /// whichever season it displays (<see cref="ShowPage.DisplayedSeasonId"/>). Caching it again
    /// under that season's own id — at that season's own lifetime, latest or past — means opening
    /// the displayed season next costs nothing. Skipped when the two ids already coincide: nothing
    /// would be gained by overwriting the very entry <see cref="MakoPageSource.GetAsync"/> just
    /// wrote, and doing so would silently replace the show page's own (12h) lifetime with a
    /// season's (6h or 7d) one.
    /// </summary>
    private void RememberDisplayedSeason(ShowPage showPage, string showJson, string fetchedPagePath, CatalogueLifetimes lifetimes)
    {
        if (showPage.DisplayedSeasonId is not string displayedSeasonId || displayedSeasonId == fetchedPagePath)
        {
            return;
        }

        TimeSpan displayedSeasonLifetime = LifetimeOf(showJson, displayedSeasonId, lifetimes);
        _pages.Remember(displayedSeasonId, showJson, displayedSeasonLifetime);
    }

    /// <summary>
    /// The show page's lifetime — decided only after the page has proven to be a mako page at
    /// all. <see cref="MakoPageSource.GetAsync"/> asks for the lifetime before it caches, so
    /// throwing from here is what keeps an HTML error page mako answered with status 200 out of
    /// the persisted cache for the next 12 hours. The season path's own lifetime does the same
    /// implicitly, by parsing the page to find out whether it is the latest season.
    /// </summary>
    private static Func<string, TimeSpan> ShowPageLifetimeOf(CatalogueLifetimes lifetimes) =>
        showPageJson =>
        {
            MakoPages.ParseShowPage(showPageJson);
            return lifetimes.ShowPage;
        };

    /// <summary>
    /// The menu is parsed with <see cref="MakoIndex.ParseGenres"/> before a lifetime is returned,
    /// so an HTML error page mako answered with status 200 is never written to the cache (same
    /// rule as the show page). A menu that parses but names no genre is rejected the same way —
    /// mako's real genres are never empty, and an empty root answer would make Jellyfin delete
    /// every show it holds.
    /// </summary>
    private static Func<string, TimeSpan> MenuLifetimeOf(TimeSpan lifetime) =>
        menuJson =>
        {
            if (MakoIndex.ParseGenres(menuJson).Count == 0)
            {
                throw new FormatException(EmptyGenreMenuMessage);
            }

            return lifetime;
        };

    /// <summary>Same rule as <see cref="MenuLifetimeOf"/>, validating a genre's own list with
    /// <see cref="MakoIndex.ParseEntries"/> instead: a list that parses but names no show is a
    /// failure too, since a shorter root answer would make Jellyfin delete the shows it lacks.</summary>
    private static Func<string, TimeSpan> ListLifetimeOf(TimeSpan lifetime) =>
        listJson =>
        {
            if (MakoIndex.ParseEntries(listJson).Count == 0)
            {
                throw new FormatException(EmptyGenreListMessage);
            }

            return lifetime;
        };

    private static TimeSpan LifetimeOf(string seasonPageJson, string seasonId, CatalogueLifetimes lifetimes) =>
        MakoPages.IsLatestSeason(seasonPageJson, seasonId) ? lifetimes.CurrentSeason : lifetimes.PastSeason;

    /// <summary>The season number comes from the fetched page's own <c>seasons[]</c> entry for
    /// <paramref name="seasonId"/>, never from the caller — falls back to 1 when the page carries
    /// no such entry.</summary>
    private static int SeasonNumberOf(string seasonPageJson, string seasonId)
    {
        MakoSeason? season = MakoPages.ParseShowPage(seasonPageJson).Seasons.FirstOrDefault(candidate => candidate.PageUrl == seasonId);
        return season?.Number ?? FallbackSeasonNumber;
    }

    /// <summary>Only peeks at the cached menu and lists — a season open must not turn into an index
    /// burst. A show no list names (a stale folder id) gets episodes with no series name.</summary>
    private string ShowNameOf(string showId)
    {
        if (!_pages.TryGetCached(MakoIndexPaths.GenreMenu, out string? menuJson))
        {
            return UnknownShowName;
        }

        foreach (Genre genre in ParseOrEmpty(() => MakoIndex.ParseGenres(menuJson)))
        {
            if (_pages.TryGetCached(genre.PagePath, out string? listJson)
                && ParseOrEmpty(() => MakoIndex.ParseEntries(listJson)).FirstOrDefault(entry => entry.PagePath == showId) is { } entry)
            {
                return entry.Name;
            }
        }

        return UnknownShowName;
    }

    private IReadOnlyList<T> ParseOrEmpty<T>(Func<IReadOnlyList<T>> parse)
    {
        try
        {
            return parse();
        }
        catch (FormatException)
        {
            _logger.LogWarning(CachedIndexPageUnparseableWarning);
            return [];
        }
    }

    /// <exception cref="StreamUnavailableException">
    /// <paramref name="episodeId"/> is not a well-formed <see cref="EpisodeKey"/> — surfaced as the
    /// catalogue's own exception type rather than the parser's raw <see cref="FormatException"/>,
    /// so every caller of <see cref="ICatalog"/> sees one family of playback failures.
    /// </exception>
    private static EpisodeKey ParseEpisodeIdOrThrow(string episodeId)
    {
        try
        {
            return EpisodeKey.Parse(episodeId);
        }
        catch (FormatException exception)
        {
            throw new StreamUnavailableException($"mako episode id '{episodeId}' is not playable", exception);
        }
    }
}
