using Jellyfin.Plugin.IlVod.Hls;

namespace Jellyfin.Plugin.IlVod.Providers.Mako;

// mako's content in its own terms: what Source/ reads off mako's pages and Transform/ turns into
// the shared model. Neither half knows the other; these records are all they share.

/// <summary>A category list in mako's genre menu — a real genre, or a provider list allowlisted
/// as one (the news) — and the page path of its show list. Its name becomes its shows' genre.</summary>
public sealed record Genre(string Name, string PagePath);

/// <summary>One show in a show list: mako's page path for it (its id), name and poster.</summary>
public sealed record IndexEntry(string PagePath, string Name, string? PosterUrl);

/// <summary>One category and every show its list names, in mako's order.</summary>
public sealed record MakoGenreList(string GenreName, IReadOnlyList<IndexEntry> Entries);

/// <summary>Everything the channel's root is built from: each category's list, and mako's full
/// show list, which also names the shows no category list does.</summary>
public sealed record MakoShowIndex(IReadOnlyList<MakoGenreList> Genres, IReadOnlyList<IndexEntry> AllShows);

/// <summary>A season as a show page lists it. <paramref name="Number"/> is read from the title
/// ("עונה 3"), or the season's position when the title names none.</summary>
public sealed record MakoSeason(string PageUrl, int Number, string Title);

/// <summary>One playable entry in a season page's episode tab, as mako wrote it.</summary>
/// <param name="ExtraInfo">mako's free-text line under the title, which carries the episode
/// number (<c>פרק N</c>) and air date (<c>@dd.mm.yy</c>) when mako states them.</param>
public sealed record MakoEpisodeEntry(EpisodeKey Key, string Title, string? ExtraInfo, string? ThumbnailUrl);

/// <summary>A season page's episodes, with the show's name and the season's number — neither of
/// which the episode entries carry themselves.</summary>
public sealed record MakoSeasonPage(string ShowName, int SeasonNumber, IReadOnlyList<MakoEpisodeEntry> Episodes);

/// <summary>What pressing play resolved to: the final (possibly signed) URL, the stream's best
/// variant when the master playlist could be read, and the length when it could be measured.</summary>
public sealed record MakoResolvedStream(string Url, HlsVariant? Variant, TimeSpan? Duration);
