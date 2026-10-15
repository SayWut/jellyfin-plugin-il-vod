using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.IlVod.Providers.Reshet13.Source;

/// <summary>
/// Reads Kaltura's assets into Reshet 13's records. Pure and tolerant, like <c>MakoPages</c>: a
/// missing or wrongly typed field becomes <c>null</c> (or is left out of a list), and an asset that
/// lacks what identifies it is skipped. Only a single asset asked for by id is refused outright,
/// since there is nothing to fall back to.
/// </summary>
public static partial class KalturaAssets
{
    /// <summary>The one media file that plays without DRM, despite its name.</summary>
    private const string HlsMediaFileType = "Hls_fairplay";

    public static Reshet13ShowIndex ParseIndex(IEnumerable<JsonElement> seriesAssets) =>
        new([.. seriesAssets.Select(TryParseSeries).OfType<Reshet13Series>()]);

    /// <summary>
    /// A series' episodes, in Kaltura's order. One that names another series is dropped: the query
    /// asked for <paramref name="seriesId"/>'s alone, so it can only be a stray. One that names no
    /// series at all is kept — it answered that query.
    /// </summary>
    public static IReadOnlyList<Reshet13Episode> ParseEpisodes(IEnumerable<JsonElement> episodeAssets, string seriesId) =>
        [.. episodeAssets
            .Where(asset => SeriesIdOf(asset) is null || SeriesIdOf(asset) == seriesId)
            .Select(TryParseEpisode)
            .OfType<Reshet13Episode>()];

    /// <exception cref="FormatException"><paramref name="asset"/> is not an asset with an id and a name.</exception>
    public static Reshet13Episode ParseEpisode(JsonElement asset) =>
        TryParseEpisode(asset) ?? throw new FormatException("Kaltura's answer is not an episode asset");

    /// <summary>Season assets' names by their <c>SeasonNumber</c> index; the first name wins.</summary>
    public static IReadOnlyDictionary<int, string> ParseSeasonNames(IEnumerable<JsonElement> seasonAssets)
    {
        Dictionary<int, string> names = [];
        foreach (JsonElement asset in seasonAssets)
        {
            if (MetaSortIndex(asset, "SeasonNumber") is int seasonIndex && NonBlank(KalturaJson.String(asset, "name")) is string name)
            {
                names.TryAdd(seasonIndex, name);
            }
        }

        return names;
    }

    private static Reshet13Series? TryParseSeries(JsonElement asset) =>
        SeriesIdOf(asset) is string seriesId && NonBlank(KalturaJson.String(asset, "name")) is string name
            ? new Reshet13Series(seriesId, name, OverviewOf(asset), ImagesOf(asset), TagValues(asset, "Genre"))
            : null;

    /// <summary>The long summary when there is one; the short description otherwise.</summary>
    private static string? OverviewOf(JsonElement asset) =>
        NonBlank(MetaString(asset, "LongSummary")) ?? NonBlank(KalturaJson.String(asset, "description"));

    private static Reshet13Episode? TryParseEpisode(JsonElement asset)
    {
        if (AssetIdOf(asset) is not string assetId || NonBlank(KalturaJson.String(asset, "name")) is not string name)
        {
            return null;
        }

        JsonElement? hlsFile = HlsMediaFileOf(asset);
        return new Reshet13Episode(
            assetId,
            name,
            MetaSortIndex(asset, "SeasonNumber"),
            MetaSortIndex(asset, "EpisodeNumber"),
            TagValues(asset, "SeasonName").FirstOrDefault(),
            TagValues(asset, "SeriesName").FirstOrDefault(),
            hlsFile is JsonElement file ? DurationOf(file) : null,
            ImagesOf(asset),
            hlsFile is JsonElement media ? EntryIdOf(media) : null);
    }

    /// <summary>The asset's own id, which Kaltura sends as a number.</summary>
    private static string? AssetIdOf(JsonElement asset) =>
        KalturaJson.WholeNumber(KalturaJson.Property(asset, "id"))?.ToString(CultureInfo.InvariantCulture);

    private static string? SeriesIdOf(JsonElement asset) => NonBlank(MetaString(asset, "SeriesID"));

    private static JsonElement? HlsMediaFileOf(JsonElement asset) =>
        KalturaJson.Items(asset, "mediaFiles")
            .Where(file => KalturaJson.String(file, "type") == HlsMediaFileType)
            .Select(file => (JsonElement?)file)
            .FirstOrDefault();

    /// <summary>Kaltura states the length in whole seconds, and 0 when it does not know it.</summary>
    private static TimeSpan? DurationOf(JsonElement mediaFile) =>
        KalturaJson.WholeNumber(KalturaJson.Property(mediaFile, "duration")) is long seconds and > 0
            ? TimeSpan.FromSeconds(seconds)
            : null;

    /// <summary>The video's OVP entry id, the <c>/entryId/…/</c> segment of the media file's URL.</summary>
    private static string? EntryIdOf(JsonElement mediaFile) =>
        KalturaJson.String(mediaFile, "url") is string url && EntryIdSegment().Match(url) is { Success: true } match
            ? match.Groups[1].Value
            : null;

    private static IReadOnlyList<KalturaImage> ImagesOf(JsonElement asset) =>
        [.. KalturaJson.Items(asset, "images")
            .Where(image => KalturaJson.String(image, "url") is not null && KalturaJson.String(image, "ratio") is not null)
            .Select(image => new KalturaImage(KalturaJson.String(image, "url")!, KalturaJson.String(image, "ratio")!))];

    private static JsonElement? MetaValue(JsonElement asset, string meta) =>
        KalturaJson.Object(asset, "metas") is JsonElement metas && KalturaJson.Object(metas, meta) is JsonElement value
            ? KalturaJson.Property(value, "value")
            : null;

    private static string? MetaString(JsonElement asset, string meta) =>
        MetaValue(asset, meta) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    /// <summary>A <c>SeasonNumber</c> or <c>EpisodeNumber</c> sort index. A negative one is no
    /// index: a season id is the index's digits, and a sign would make a season that never opens.</summary>
    private static int? MetaSortIndex(JsonElement asset, string meta) =>
        KalturaJson.WholeNumber(MetaValue(asset, meta)) is long number and >= 0 and <= int.MaxValue ? (int)number : null;

    /// <summary>A tag's values (<c>tags.Genre.objects[].value</c>), as the asset lists them.</summary>
    private static IReadOnlyList<string> TagValues(JsonElement asset, string tag) =>
        KalturaJson.Object(asset, "tags") is JsonElement tags && KalturaJson.Object(tags, tag) is JsonElement values
            ? [.. KalturaJson.Items(values, "objects").Select(value => NonBlank(KalturaJson.String(value, "value"))).OfType<string>()]
            : [];

    private static string? NonBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    [GeneratedRegex("/entryId/([^/]+)/", RegexOptions.CultureInvariant)]
    private static partial Regex EntryIdSegment();
}
