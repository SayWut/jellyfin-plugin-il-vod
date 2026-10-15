using Jellyfin.Plugin.IlVod.Core;

namespace Jellyfin.Plugin.IlVod.Tests.Support;

/// <summary>
/// A hand-written <see cref="ICatalog"/> fake for <c>IlVodChannelTests</c>: a fixed show/season/
/// episode shape to map, a settable stream link for play, and two settable failures so a test can
/// prove <c>IlVodChannel</c> translates a catalogue failure into either an empty browse result or a
/// propagated play error, without a real mako call anywhere in the plugin's own test suite.
/// </summary>
public sealed class FakeCatalog : ICatalog
{
    public const string PlayableShowId = "shemesh";
    public const string CardOnlyShowId = "eretz_nehederet";

    private const string PlayableShowName = "שמש";
    private const int EpisodesPerSeason = 3;


    private static readonly IReadOnlyList<Season> DefaultSeasons =
    [
        new Season("shemesh-s1", 1, "עונה 1"),
        new Season("shemesh-s2", 2, "עונה 2"),
    ];

    /// <summary>The category names every show is listed with, as a provider would spell them.</summary>
    public IReadOnlyList<string> Genres { get; set; } = [];

    /// <summary>Thrown from every browse method (<see cref="GetShowsAsync"/>,
    /// <see cref="GetSeasonsAsync"/>, <see cref="GetEpisodesAsync"/>) instead of answering, when set —
    /// standing in for mako refusing a page request.</summary>
    public Exception? BrowseFailure { get; set; }

    /// <summary>Thrown from <see cref="GetStreamLinkAsync"/> instead of answering, when set.</summary>
    public Exception? StreamLinkFailure { get; set; }

    /// <summary>What <see cref="GetStreamLinkAsync"/> answers when <see cref="StreamLinkFailure"/> is not set.</summary>
    public StreamLink? StreamLink { get; set; }

    /// <summary>How many times any browse method was called, failed or not.</summary>
    public int BrowseCalls { get; private set; }

    /// <summary>The show and season id <see cref="GetEpisodesAsync"/> was last called with.</summary>
    public (string ShowId, string SeasonId)? LastEpisodesRequest { get; private set; }

    public Task<IReadOnlyList<Show>> GetShowsAsync(CancellationToken cancellationToken)
    {
        BrowseCalls++;
        IReadOnlyList<Show> shows =
        [
            new Show(PlayableShowId, PlayableShowName, PosterUrl: null, Overview: null, Genres: Genres),
            new Show(CardOnlyShowId, "ארץ נהדרת", PosterUrl: null, Overview: null, Genres: Genres),
        ];
        return BrowseFailure is { } failure ? Task.FromException<IReadOnlyList<Show>>(failure) : Task.FromResult(shows);
    }

    public Task<IReadOnlyList<Season>> GetSeasonsAsync(string showId, CancellationToken cancellationToken)
    {
        BrowseCalls++;
        if (BrowseFailure is { } failure)
        {
            return Task.FromException<IReadOnlyList<Season>>(failure);
        }

        IReadOnlyList<Season> seasons = showId == PlayableShowId ? DefaultSeasons : [];
        return Task.FromResult(seasons);
    }

    public Task<IReadOnlyList<Episode>> GetEpisodesAsync(string showId, string seasonId, CancellationToken cancellationToken)
    {
        BrowseCalls++;
        LastEpisodesRequest = (showId, seasonId);
        if (BrowseFailure is { } failure)
        {
            return Task.FromException<IReadOnlyList<Episode>>(failure);
        }

        Season? season = DefaultSeasons.FirstOrDefault(candidate => candidate.Id == seasonId);
        IReadOnlyList<Episode> episodes = season is null ? [] : EpisodesOf(season);
        return Task.FromResult(episodes);
    }

    public Task<StreamLink> GetStreamLinkAsync(string episodeId, CancellationToken cancellationToken)
    {
        if (StreamLinkFailure is { } failure)
        {
            return Task.FromException<StreamLink>(failure);
        }

        return Task.FromResult(StreamLink ?? throw new InvalidOperationException($"{nameof(FakeCatalog)}.{nameof(StreamLink)} was not set for this test."));
    }

    private static IReadOnlyList<Episode> EpisodesOf(Season season) =>
        Enumerable.Range(1, EpisodesPerSeason)
            .Select(number => new Episode(
                $"{season.Id}-e{number}",
                PlayableShowName,
                season.Number,
                number,
                $"פרק בדיקה {number}",
                AirDate: null,
                Duration: TimeSpan.FromMinutes(25),
                ThumbnailUrl: null))
            .ToList();
}
