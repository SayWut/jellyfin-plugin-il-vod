using System.Text.Json.Nodes;

namespace Jellyfin.Plugin.IlVod.Providers.Reshet13.Source;

/// <summary>
/// The one place every question asked of Kaltura is spelled. Kaltura caches its answers for 300 s
/// keyed on the request body, so the same question must always be the same bytes: each body is
/// built here, from constants and checked ids only, with its fields in a fixed order.
/// </summary>
public static class KalturaQueries
{
    /// <summary>Kaltura's largest page; anything larger is silently clamped to it. 500 holds every
    /// series in one page, and a show's every episode in one page for all but the longest.</summary>
    public const int ListPageSize = 500;

    /// <summary>A show has a handful of season assets; the app itself asks for 50.</summary>
    public const int SeasonAssetPageSize = 50;

    private const string SeriesAssetType = "1259";
    private const string SeasonAssetType = "1264";
    private const string EpisodeAssetType = "1268";

    private const string AssetService = "asset";
    private const string ListAction = "list";
    private const string GetAction = "get";

    /// <summary>One page of every series, by name so that pages never overlap.</summary>
    public static KalturaQuery SeriesPage(int pageIndex) =>
        new(AssetService, ListAction, $"the series list page {pageIndex}", new JsonObject
        {
            ["filter"] = new JsonObject
            {
                ["objectType"] = "KalturaSearchAssetFilter",
                ["typeIn"] = SeriesAssetType,
                ["orderBy"] = "NAME_ASC",
            },
            ["pager"] = Pager(pageIndex, ListPageSize),
        });

    /// <summary>One page of a series' episodes, ordered by their <c>EpisodeNumber</c> sort index.</summary>
    /// <remarks>The index ties across seasons, so a tied episode may sit on both sides of a page
    /// boundary; <see cref="KalturaOttClient.ListAllAsync"/> keeps it once and checks the total.
    /// Kaltura offers no order without ties: names, creation dates and a creation-date tie-breaker
    /// all repeat within a long show (checked live on series 57, 2026-10-16).</remarks>
    /// <exception cref="ArgumentException"><paramref name="seriesId"/> is not a Kaltura id.</exception>
    public static KalturaQuery EpisodesPage(string seriesId, int pageIndex) =>
        new(AssetService, ListAction, $"series {seriesId} episodes page {pageIndex}", new JsonObject
        {
            ["filter"] = new JsonObject
            {
                ["objectType"] = "KalturaSearchAssetFilter",
                ["typeIn"] = EpisodeAssetType,
                ["kSql"] = AssetsOfSeries(seriesId, EpisodeAssetType),
                ["dynamicOrderBy"] = new JsonObject
                {
                    ["objectType"] = "KalturaDynamicOrderBy",
                    ["name"] = "EpisodeNumber",
                    ["orderBy"] = "META_ASC",
                },
            },
            ["pager"] = Pager(pageIndex, ListPageSize),
        });

    /// <summary>One page of a series' season assets — read only for their names.</summary>
    /// <exception cref="ArgumentException"><paramref name="seriesId"/> is not a Kaltura id.</exception>
    public static KalturaQuery SeasonAssetsPage(string seriesId, int pageIndex) =>
        new(AssetService, ListAction, $"series {seriesId} season assets page {pageIndex}", new JsonObject
        {
            ["filter"] = new JsonObject
            {
                ["objectType"] = "KalturaSearchAssetFilter",
                ["kSql"] = AssetsOfSeries(seriesId, SeasonAssetType),
            },
            ["pager"] = Pager(pageIndex, SeasonAssetPageSize),
        });

    /// <summary>One asset by its own id.</summary>
    /// <exception cref="ArgumentException"><paramref name="assetId"/> is not a Kaltura id.</exception>
    public static KalturaQuery Asset(string assetId) =>
        new(AssetService, GetAction, $"asset {RequireWellFormed(assetId)}", new JsonObject
        {
            ["id"] = assetId,
            ["assetReferenceType"] = "media",
        });

    private static string AssetsOfSeries(string seriesId, string assetType) =>
        $"(and SeriesID = '{RequireWellFormed(seriesId)}' asset_type = '{assetType}')";

    private static JsonObject Pager(int pageIndex, int pageSize) => new()
    {
        ["objectType"] = "KalturaFilterPager",
        ["pageIndex"] = pageIndex,
        ["pageSize"] = pageSize,
    };

    /// <summary>The guard that keeps an id from Jellyfin from ever rewriting a <c>kSql</c> filter.</summary>
    private static string RequireWellFormed(string id) =>
        KalturaIds.IsWellFormed(id)
            ? id
            : throw new ArgumentException($"'{id}' is not a Kaltura id", nameof(id));
}
