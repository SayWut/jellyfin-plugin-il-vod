using System.Globalization;

namespace Jellyfin.Plugin.IlVod.Providers.Reshet13.Source;

/// <summary>Every address Reshet 13's content is fetched from. One goes into a log line or an
/// exception message only through <c>SafeUrl.Redacted</c>; Kaltura's session token travels in the
/// request body, never in a URL, so nothing else needs scrubbing.</summary>
public static class Reshet13Urls
{
    private const string KalturaServiceUrlTemplate = "https://5031.frp1.ott.kaltura.com/api_v3/service/{0}/action/{1}";

    /// <summary>The clean (ad-free) master playlist on Kaltura's own CDN, under Reshet 13's video
    /// partner 2748741 — what the app itself builds, rather than the ad-stitched
    /// <c>g-mana.live</c> URL the media file names.</summary>
    private const string CleanManifestUrlTemplate =
        "https://cdnapisec.kaltura.com/p/2748741/sp/274874100/playManifest/entryId/{0}/format/applehttp/protocol/https/a.m3u8";

    public static string Service(string service, string action) =>
        string.Format(CultureInfo.InvariantCulture, KalturaServiceUrlTemplate, service, action);

    public static string CleanManifest(string entryId) =>
        string.Format(CultureInfo.InvariantCulture, CleanManifestUrlTemplate, Uri.EscapeDataString(entryId));
}
