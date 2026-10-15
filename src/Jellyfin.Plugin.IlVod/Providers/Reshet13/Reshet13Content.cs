using Jellyfin.Plugin.IlVod.Hls;

namespace Jellyfin.Plugin.IlVod.Providers.Reshet13;

// Reshet 13's content in its own terms, as its Kaltura OTT catalogue answers it: what Source/
// reads off Kaltura's answers and Transform/ turns into the shared model. Neither half knows the
// other; these records are all they share.

/// <summary>One image as Kaltura lists it: the image's base URL, to which a size is appended, and its
/// aspect ratio as Kaltura spells it (<c>2x3</c>, <c>9x16</c>, <c>16x9</c>, <c>1x1</c>).</summary>
public sealed record KalturaImage(string Url, string Ratio);

/// <summary>One series asset. <paramref name="SeriesId"/> is the asset's <c>SeriesID</c> meta, the
/// key every season and episode query joins on — not the asset's own id.</summary>
/// <param name="Genres">The series' <c>Genre</c> tags, as Reshet 13 spells them.</param>
public sealed record Reshet13Series(
    string SeriesId,
    string Name,
    string? Overview,
    IReadOnlyList<KalturaImage> Images,
    IReadOnlyList<string> Genres);

/// <summary>Every series in the catalogue, in the order Kaltura answered them.</summary>
public sealed record Reshet13ShowIndex(IReadOnlyList<Reshet13Series> Series);

/// <summary>One episode asset.</summary>
/// <param name="AssetId">The Kaltura asset id: the episode's id, and what <c>asset/get</c> takes.</param>
/// <param name="SeasonIndex">The <c>SeasonNumber</c> meta — a sort index, not the season's displayed
/// number, and its direction differs between shows; <c>null</c> when the episode has none.</param>
/// <param name="EpisodeIndex">The <c>EpisodeNumber</c> meta — a sort index too; <c>null</c> when absent.</param>
/// <param name="SeasonName">The <c>SeasonName</c> tag (<c>עונה 8</c>), which many episodes lack.</param>
/// <param name="SeriesName">The <c>SeriesName</c> tag: the show's name, as the episode carries it.</param>
/// <param name="Duration">The HLS media file's length; <c>null</c> when it states none (or zero).</param>
/// <param name="EntryId">The video's Kaltura OVP entry id (<c>1_f9hhlezq</c>), read from the HLS
/// media file's URL; <c>null</c> when the episode has no HLS file.</param>
public sealed record Reshet13Episode(
    string AssetId,
    string Name,
    int? SeasonIndex,
    int? EpisodeIndex,
    string? SeasonName,
    string? SeriesName,
    TimeSpan? Duration,
    IReadOnlyList<KalturaImage> Images,
    string? EntryId);

/// <summary>Everything a show's folder is built from: every episode of the series — the only way
/// to learn which seasons exist, since Kaltura's season assets are incomplete — and the names its
/// season assets give, by <see cref="Reshet13Episode.SeasonIndex"/>.</summary>
public sealed record Reshet13ShowPage(
    IReadOnlyDictionary<int, string> SeasonAssetNames,
    IReadOnlyList<Reshet13Episode> Episodes);

/// <summary>One season's episodes, with the show's name and the season's asset name (when the show
/// has a season asset for it) — neither of which every episode carries itself.</summary>
public sealed record Reshet13SeasonPage(
    string ShowName,
    int? SeasonIndex,
    string? SeasonAssetName,
    IReadOnlyList<Reshet13Episode> Episodes);

/// <summary>What pressing play resolved to: the clean (ad-free) master playlist URL, its best variant
/// when the playlist could be read, and the episode's length when Kaltura states it.</summary>
public sealed record Reshet13Stream(string ManifestUrl, HlsVariant? Variant, TimeSpan? Duration);
