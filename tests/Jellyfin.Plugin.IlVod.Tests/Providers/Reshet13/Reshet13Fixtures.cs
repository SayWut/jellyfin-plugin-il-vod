using Jellyfin.Plugin.IlVod.Providers.Reshet13.Source;
using Jellyfin.Plugin.IlVod.Tests.Support;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Reshet13;

/// <summary>
/// The saved Kaltura answers and the body predicates that tell Reshet 13's requests apart — every
/// Kaltura call POSTs to one of a few URLs, so only the body says which question it asks.
/// </summary>
internal static class Reshet13Fixtures
{
    public const string SeriesId = "57";
    public const string KnownAssetId = "4471744";
    public const string KnownEntryId = "1_3g4b8w3i";
    public const string AssetGetId = "2361602";
    public const string AssetGetEntryId = "1_iilurenz";
    public const string Ks = "TEST-KS";

    public static readonly string LoginJson = FixtureFile.ReadText("reshet13-login.json");
    public static readonly string SeriesJson = FixtureFile.ReadText("reshet13-series.json");
    public static readonly string EpisodesJson = FixtureFile.ReadText("reshet13-episodes-57.json");
    public static readonly string SeasonsJson = FixtureFile.ReadText("reshet13-seasons-57.json");
    public static readonly string AssetGetJson = FixtureFile.ReadText("reshet13-asset-get.json");
    public static readonly string MasterPlaylist = FixtureFile.ReadText("reshet13-master.m3u8");
    public static readonly string Error500015Json = FixtureFile.ReadText("reshet13-error-500015.json");
    public static readonly string Error500004Json = FixtureFile.ReadText("reshet13-error-500004.json");

    public static readonly string LoginUrl = Reshet13Urls.Service("ottUser", "anonymousLogin");
    public static readonly string ListUrl = Reshet13Urls.Service("asset", "list");
    public static readonly string GetUrl = Reshet13Urls.Service("asset", "get");

    public static readonly string KnownManifestUrl = Reshet13Urls.CleanManifest(KnownEntryId);
    public static readonly string AssetGetManifestUrl = Reshet13Urls.CleanManifest(AssetGetEntryId);

    public static bool Any(string body) => true;

    public static bool IsSeriesList(string body) => body.Contains("\"typeIn\":\"1259\"", StringComparison.Ordinal);

    public static bool IsEpisodesOf57(string body) =>
        body.Contains("SeriesID = '57' asset_type = '1268'", StringComparison.Ordinal);

    public static bool IsSeasonAssetsOf57(string body) =>
        body.Contains("SeriesID = '57' asset_type = '1264'", StringComparison.Ordinal);

    public static bool IsAssetGet(string body) => body.Contains($"\"id\":\"{AssetGetId}\"", StringComparison.Ordinal);

    public static Func<string, bool> IsPage(int pageIndex) =>
        body => body.Contains($"\"pageIndex\":{pageIndex},", StringComparison.Ordinal);

    /// <summary>Every saved answer in place: login, the series list, show 57's episodes and season
    /// assets, <see cref="AssetGetId"/>'s asset, and the clean manifests of both entry ids.</summary>
    public static FakeReshet13Http SeededHttp()
    {
        FakeReshet13Http http = new();
        http.RespondToPost(LoginUrl, Any, LoginJson);
        http.RespondToPost(ListUrl, IsSeriesList, SeriesJson);
        http.RespondToPost(ListUrl, IsEpisodesOf57, EpisodesJson);
        http.RespondToPost(ListUrl, IsSeasonAssetsOf57, SeasonsJson);
        http.RespondToPost(GetUrl, IsAssetGet, AssetGetJson);
        http.RespondToGet(KnownManifestUrl, MasterPlaylist);
        http.RespondToGet(AssetGetManifestUrl, MasterPlaylist);
        return http;
    }
}
