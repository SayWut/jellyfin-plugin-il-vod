using Jellyfin.Plugin.IlVod.Hls;

namespace Jellyfin.Plugin.IlVod.Providers.Kan;

// Kan 11's content in its own terms, as kan.org.il answers it: what Source/ reads off Kan's genre
// pages, program feeds and episode pages, and Transform/ turns into the shared model. Neither half
// knows the other; these records are all they share.

/// <summary>One show, as the Kan BOX genre pages list it.</summary>
/// <param name="ProgramId">The program's numeric id (a card's <c>data-item-id</c>): the show's id.</param>
/// <param name="ImageUrl">The card's 16:9 image, absolute and without a size; <c>null</c> when it has none.</param>
/// <param name="PosterUrl">The lobby's 2:3 poster, absolute and without a size; <c>null</c> when the
/// lobby's rails, which are capped, do not show the program.</param>
/// <param name="Overview">The lobby card's one-line synopsis; <c>null</c> when the lobby does not show it.</param>
/// <param name="Genres">Every genre page the program is listed on, named as Kan names the genre.</param>
public sealed record KanShowCard(
    string ProgramId,
    string Name,
    string? ImageUrl,
    string? PosterUrl,
    string? Overview,
    IReadOnlyList<string> Genres);

/// <summary>Every Kan 11 show in the catalogue, in the order the genre pages first listed them.</summary>
public sealed record KanShowIndex(IReadOnlyList<KanShowCard> Shows);

/// <summary>One playable episode of a program feed — never one of its audio entries.</summary>
/// <param name="EntryId">The episode page's id: unique across Kan, and with the program id all play needs.</param>
/// <param name="Season">The feed's season number; <c>null</c> when it states none (0 or nothing) —
/// a show with no seasons, or an extra (a trailer, a clip).</param>
/// <param name="EpisodeNumber">The feed's episode number; <c>null</c> when it states none.</param>
/// <param name="BroadcastDate">The date it aired; <c>null</c> when the feed states none. Never the
/// feed's <c>published</c>, which for older episodes is the date they were imported.</param>
/// <param name="Duration">Its length; <c>null</c> when the feed states none (or zero).</param>
/// <param name="ThumbnailUrl">The feed's 16:9 still, absolute; <c>null</c> when it has none.</param>
public sealed record KanEntry(
    string EntryId,
    string Title,
    int? Season,
    int? EpisodeNumber,
    DateTime? BroadcastDate,
    TimeSpan? Duration,
    string? ThumbnailUrl);

/// <summary>A program's feed: its name and every playable episode it lists, newest first. Kan caps
/// the feed at its 200 most recent entries, so a longer-running show is its latest 200.</summary>
public sealed record KanShowPage(string ProgramId, string ShowName, IReadOnlyList<KanEntry> Episodes)
{
    /// <summary>Whether any episode states a season. Here, beside the record, because the source (a
    /// season page) and the transformer (the season list) must agree on it: it decides whether the
    /// unnumbered episodes are the show's only season or its extras.</summary>
    public bool HasNumberedSeasons() => Episodes.Any(episode => episode.Season is not null);
}

/// <summary>One season's episodes, with the show's name and whether the show has numbered seasons
/// besides — which decides what the unnumbered season is called.</summary>
/// <param name="Season">The season number; <c>null</c> for the episodes that state none.</param>
public sealed record KanSeasonPage(
    string ProgramId,
    string ShowName,
    int? Season,
    bool ShowHasNumberedSeasons,
    IReadOnlyList<KanEntry> Episodes);

/// <summary>What pressing play resolved to: the master playlist's URL, its best variant's
/// description and where ffmpeg finds that variant's video and the audio (both <c>null</c> when the
/// playlist could not be read or numbers its tracks otherwise), and the episode's length.</summary>
public sealed record KanStream(string Url, HlsVariant? Variant, HlsStreamIndices? StreamIndices, TimeSpan? Duration);
