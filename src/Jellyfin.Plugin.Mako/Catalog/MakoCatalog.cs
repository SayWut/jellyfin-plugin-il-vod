using System.Text.Json;
using Jellyfin.Plugin.Mako.Cache;
using Jellyfin.Plugin.Mako.MakoApi;
using Jellyfin.Plugin.Mako.Model;

namespace Jellyfin.Plugin.Mako.Catalog;

/// <summary>The one show step B plays, and the folder name a user sees for it.</summary>
public sealed record PinnedShow(string PagePath, string Name);

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

    /// <param name="clock">
    /// Not read directly: <paramref name="streamLinks"/> already carries the same clock it was
    /// constructed with, which is what actually decides freshness. Accepted here anyway so a
    /// caller wiring this catalog's dependencies (task B10) can pass the one plugin-wide
    /// <see cref="IClock"/> everywhere without a special case for this class.
    /// </param>
    public MakoCatalog(
        MakoPageSource pages,
        StreamResolver resolver,
        TimedCache streamLinks,
        IClock clock,
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
    /// page cache — never fetches — so opening the channel costs mako nothing.
    /// </summary>
    public Task<IReadOnlyList<Show>> GetShowsAsync(CancellationToken cancellationToken)
    {
        PinnedShow pinned = _readPinnedShow();
        Show show = _pages.TryGetCached(pinned.PagePath, out string? cachedShowJson)
            ? Enrich(pinned, cachedShowJson)
            : new Show(pinned.PagePath, pinned.Name, PosterUrl: null, Overview: null, Genres: []);

        return Task.FromResult<IReadOnlyList<Show>>([show]);
    }

    public async Task<IReadOnlyList<Season>> GetSeasonsAsync(string showId, CancellationToken cancellationToken)
    {
        PinnedShow pinned = _readPinnedShow();
        if (showId != pinned.PagePath)
        {
            return [];
        }

        CacheLifetimes lifetimes = _readLifetimes();
        string showJson = await _pages.GetAsync(showId, _ => lifetimes.ShowPage, cancellationToken).ConfigureAwait(false);
        ShowPage showPage = MakoPages.ParseShowPage(showJson);

        if (showPage.CurrentSeasonId is string currentSeasonId)
        {
            _pages.Remember(currentSeasonId, showJson, lifetimes.CurrentSeason);
        }

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

        StreamLink link = await _resolver.ResolveAsync(EpisodeKey.Parse(episodeId), cancellationToken).ConfigureAwait(false);
        _streamLinks.Write(episodeId, SerializeStreamLink(link), _readLifetimes().StreamLink);
        return link;
    }

    private static Show Enrich(PinnedShow pinned, string cachedShowJson)
    {
        ShowPage showPage = MakoPages.ParseShowPage(cachedShowJson);
        return new Show(pinned.PagePath, pinned.Name, showPage.PosterUrl, showPage.Overview, Genres: []);
    }

    private static TimeSpan LifetimeOf(string seasonPageJson, string seasonId, CacheLifetimes lifetimes) =>
        MakoPages.IsCurrentSeason(seasonPageJson, seasonId) ? lifetimes.CurrentSeason : lifetimes.PastSeason;

    /// <summary>The season number comes from the fetched page's own <c>seasons[]</c> entry for
    /// <paramref name="seasonId"/>, never from the caller — falls back to 1 when the page carries
    /// no such entry.</summary>
    private static int SeasonNumberOf(string seasonPageJson, string seasonId)
    {
        Season? season = MakoPages.ParseShowPage(seasonPageJson).Seasons.FirstOrDefault(candidate => candidate.Id == seasonId);
        return season?.Number ?? FallbackSeasonNumber;
    }

    private static string SerializeStreamLink(StreamLink link) => JsonSerializer.Serialize(link);

    private static StreamLink DeserializeStreamLink(string json) =>
        JsonSerializer.Deserialize<StreamLink>(json)
            ?? throw new JsonException("cached stream link deserialized to null");
}
