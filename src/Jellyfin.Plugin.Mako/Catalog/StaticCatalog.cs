using Jellyfin.Plugin.Mako.Model;

namespace Jellyfin.Plugin.Mako.Catalog;

/// <summary>
/// Step A content: fixed data and no network, so the Jellyfin side can be judged on its own.
/// </summary>
public sealed class StaticCatalog : ICatalog
{
    public const string PlayableShowId = "shemesh";
    public const string CardOnlyShowId = "eretz_nehederet";

    private const string PlayableShowName = "שמש";
    private const int EpisodesPerSeason = 3;

    // An approximation for the test fixture, so seek/resume has something to work with.
    // The real stream's length comes from mako in step B.
    private static readonly TimeSpan TestEpisodeDuration = TimeSpan.FromMinutes(25);

    private static readonly IReadOnlyList<Show> Shows =
    [
        new Show(
            PlayableShowId,
            PlayableShowName,
            "https://img.mako.co.il/2025/06/09/Shemesh_e.jpg",
            "תוכן בדיקה: שתי עונות קבועות. כל פרק מנגן את כתובת הבדיקה מהגדרות התוסף.",
            ["קומדיה"]),
        new Show(
            CardOnlyShowId,
            "ארץ נהדרת",
            "https://img.mako.co.il/2025/10/21/ERETZ2026_1_e_re.jpg",
            "תוכן בדיקה: כרטיס בלבד, כדי לבדוק סינון לפי ז'אנר.",
            ["סאטירה"]),
    ];

    private static readonly IReadOnlyList<Season> PlayableShowSeasons =
    [
        new Season("shemesh-s1", 1, "עונה 1"),
        new Season("shemesh-s2", 2, "עונה 2"),
    ];

    private readonly Func<string> _readTestStreamUrl;
    private readonly Func<string> _readTestStreamContainer;

    public StaticCatalog(Func<string> readTestStreamUrl, Func<string> readTestStreamContainer)
    {
        _readTestStreamUrl = readTestStreamUrl;
        _readTestStreamContainer = readTestStreamContainer;
    }

    public Task<IReadOnlyList<Show>> GetShowsAsync(CancellationToken cancellationToken)
        => Task.FromResult(Shows);

    public Task<IReadOnlyList<Season>> GetSeasonsAsync(string showId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Season> seasons = showId == PlayableShowId ? PlayableShowSeasons : [];
        return Task.FromResult(seasons);
    }

    public Task<IReadOnlyList<Episode>> GetEpisodesAsync(string seasonId, CancellationToken cancellationToken)
    {
        Season? season = PlayableShowSeasons.FirstOrDefault(candidate => candidate.Id == seasonId);
        IReadOnlyList<Episode> episodes = season is null ? [] : EpisodesOf(season);
        return Task.FromResult(episodes);
    }

    public Task<StreamLink> GetStreamLinkAsync(string episodeId, CancellationToken cancellationToken)
    {
        string url = _readTestStreamUrl();
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new StreamUnavailableException(
                "No Test stream URL is set. Paste a freshly signed URL in Dashboard > Plugins > Mako.");
        }

        string container = _readTestStreamContainer();
        return Task.FromResult(new StreamLink(url.Trim(), container.Trim(), StreamDescription.MakoDefault));
    }

    private static IReadOnlyList<Episode> EpisodesOf(Season season)
        => Enumerable.Range(1, EpisodesPerSeason)
            .Select(number => new Episode(
                $"{season.Id}-e{number}",
                PlayableShowName,
                season.Number,
                number,
                $"פרק בדיקה {number}",
                AirDate: null,
                Duration: TestEpisodeDuration,
                ThumbnailUrl: null))
            .ToList();
}
