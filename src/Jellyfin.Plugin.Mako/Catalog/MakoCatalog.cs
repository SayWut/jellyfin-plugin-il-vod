using System.Text.Json;
using Jellyfin.Plugin.Mako.Cache;
using Jellyfin.Plugin.Mako.MakoApi;
using Jellyfin.Plugin.Mako.Model;

namespace Jellyfin.Plugin.Mako.Catalog;

/// <summary>
/// The pinned show's live catalogue (spec §5.2): every method answers only what the single user
/// action calling it requires, and nothing here ever fetches ahead of that action (N1) —
/// <c>OnDemandRequestBudgetTests</c> is the test that proves it. <paramref name="readPinnedShow"/>
/// and <paramref name="readLifetimes"/> are delegates, not values, so a change on the settings
/// page (task B10) takes effect on the next call with no restart, the same pattern
/// <c>StaticCatalog</c> already used for its test stream URL.
/// </summary>
public sealed class MakoCatalog : ICatalog
{
    private const int FallbackSeasonNumber = 1;

    private readonly MakoPageSource _pages;
    private readonly StreamResolver _resolver;
    private readonly TimedCache _streamLinks;
    private readonly Func<PinnedShow> _readPinnedShow;
    private readonly Func<CacheLifetimes> _readLifetimes;

    public MakoCatalog(
        MakoPageSource pages,
        StreamResolver resolver,
        TimedCache streamLinks,
        Func<PinnedShow> readPinnedShow,
        Func<CacheLifetimes> readLifetimes)
    {
        _pages = pages;
        _resolver = resolver;
        _streamLinks = streamLinks;
        _readPinnedShow = readPinnedShow;
        _readLifetimes = readLifetimes;
    }

    /// <summary>
    /// Exactly one <see cref="Show"/> built from the pinned setting alone. Only ever peeks at the
    /// page cache — never fetches — so opening the channel costs mako nothing. A cached page that
    /// fails to parse (spec content changed under an old cache entry, or plain corruption) falls
    /// back to the name-only show instead of failing the whole channel root: a stale copy is
    /// served indefinitely by design (N2/spec §5.4), so a parse error here must never be fatal.
    /// </summary>
    public Task<IReadOnlyList<Show>> GetShowsAsync(CancellationToken cancellationToken)
    {
        PinnedShow pinned = _readPinnedShow();
        IReadOnlyList<Show> shows = IsConfigured(pinned) ? [BuildShow(pinned)] : [];
        return Task.FromResult(shows);
    }

    public async Task<IReadOnlyList<Season>> GetSeasonsAsync(string showId, CancellationToken cancellationToken)
    {
        PinnedShow pinned = _readPinnedShow();
        if (!IsConfigured(pinned) || showId != pinned.PagePath)
        {
            return [];
        }

        CacheLifetimes lifetimes = _readLifetimes();
        string showJson = await _pages.GetAsync(showId, ShowPageLifetimeOf(lifetimes), cancellationToken).ConfigureAwait(false);
        ShowPage showPage = MakoPages.ParseShowPage(showJson);

        RememberDisplayedSeason(showPage, showJson, showId, lifetimes);

        return showPage.Seasons;
    }

    public async Task<IReadOnlyList<Episode>> GetEpisodesAsync(string seasonId, CancellationToken cancellationToken)
    {
        PinnedShow pinned = _readPinnedShow();
        CacheLifetimes lifetimes = _readLifetimes();

        string seasonJson = await _pages
            .GetAsync(seasonId, pageJson => LifetimeOf(pageJson, seasonId, lifetimes), cancellationToken)
            .ConfigureAwait(false);

        return MakoPages.ParseEpisodes(seasonJson, pinned.Name, SeasonNumberOf(seasonJson, seasonId));
    }

    public async Task<StreamLink> GetStreamLinkAsync(string episodeId, CancellationToken cancellationToken)
    {
        CacheRead cached = _streamLinks.Read(episodeId);
        if (cached.IsFresh)
        {
            return DeserializeStreamLink(cached.Value!);
        }

        EpisodeKey episode = ParseEpisodeIdOrThrow(episodeId);
        StreamLink link = await _resolver.ResolveAsync(episode, cancellationToken).ConfigureAwait(false);
        _streamLinks.Write(episodeId, SerializeStreamLink(link), _readLifetimes().StreamLink);
        return link;
    }

    /// <summary>
    /// The page just fetched under <paramref name="fetchedPagePath"/> also carries the episodes of
    /// whichever season it displays (<see cref="ShowPage.DisplayedSeasonId"/>). Caching it again
    /// under that season's own id — at that season's own lifetime, latest or past — means opening
    /// the displayed season next costs nothing. Skipped when the two ids already coincide: nothing
    /// would be gained by overwriting the very entry <see cref="MakoPageSource.GetAsync"/> just
    /// wrote, and doing so would silently replace the show page's own (12h) lifetime with a
    /// season's (6h or 7d) one.
    /// </summary>
    private void RememberDisplayedSeason(ShowPage showPage, string showJson, string fetchedPagePath, CacheLifetimes lifetimes)
    {
        if (showPage.DisplayedSeasonId is not string displayedSeasonId || displayedSeasonId == fetchedPagePath)
        {
            return;
        }

        TimeSpan displayedSeasonLifetime = LifetimeOf(showJson, displayedSeasonId, lifetimes);
        _pages.Remember(displayedSeasonId, showJson, displayedSeasonLifetime);
    }

    private Show BuildShow(PinnedShow pinned)
    {
        if (!_pages.TryGetCached(pinned.PagePath, out string? cachedShowJson))
        {
            return NameOnlyShow(pinned);
        }

        try
        {
            ShowPage showPage = MakoPages.ParseShowPage(cachedShowJson);
            return new Show(pinned.PagePath, pinned.Name, showPage.PosterUrl, showPage.Overview, Genres: []);
        }
        catch (FormatException)
        {
            return NameOnlyShow(pinned);
        }
    }

    private static Show NameOnlyShow(PinnedShow pinned) => new(pinned.PagePath, pinned.Name, PosterUrl: null, Overview: null, Genres: []);

    /// <summary>A fresh install has nothing pinned yet: that is "no show", not a request for the
    /// empty page path.</summary>
    private static bool IsConfigured(PinnedShow pinned) => !string.IsNullOrWhiteSpace(pinned.PagePath);

    /// <summary>
    /// The show page's lifetime — decided only after the page has proven to be a mako page at
    /// all. <see cref="MakoPageSource.GetAsync"/> asks for the lifetime before it caches, so
    /// throwing from here is what keeps an HTML error page mako answered with status 200 out of
    /// the persisted cache for the next 12 hours. The season path's own lifetime does the same
    /// implicitly, by parsing the page to find out whether it is the latest season.
    /// </summary>
    private static Func<string, TimeSpan> ShowPageLifetimeOf(CacheLifetimes lifetimes) =>
        showPageJson =>
        {
            MakoPages.ParseShowPage(showPageJson);
            return lifetimes.ShowPage;
        };

    private static TimeSpan LifetimeOf(string seasonPageJson, string seasonId, CacheLifetimes lifetimes) =>
        MakoPages.IsLatestSeason(seasonPageJson, seasonId) ? lifetimes.CurrentSeason : lifetimes.PastSeason;

    /// <summary>The season number comes from the fetched page's own <c>seasons[]</c> entry for
    /// <paramref name="seasonId"/>, never from the caller — falls back to 1 when the page carries
    /// no such entry.</summary>
    private static int SeasonNumberOf(string seasonPageJson, string seasonId)
    {
        Season? season = MakoPages.ParseShowPage(seasonPageJson).Seasons.FirstOrDefault(candidate => candidate.Id == seasonId);
        return season?.Number ?? FallbackSeasonNumber;
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

    private static string SerializeStreamLink(StreamLink link) => JsonSerializer.Serialize(link);

    private static StreamLink DeserializeStreamLink(string json) =>
        JsonSerializer.Deserialize<StreamLink>(json)
            ?? throw new JsonException("cached stream link deserialized to null");
}
