using System.Globalization;

namespace Jellyfin.Plugin.IlVod.Providers.Reshet13.Source;

/// <summary>Every address Reshet 13's content is fetched from, and the one way such an address is
/// allowed into a log line or an exception message.</summary>
public static class Reshet13Urls
{
    private const string KalturaServiceUrlTemplate = "https://5031.frp1.ott.kaltura.com/api_v3/service/{0}/action/{1}";

    /// <summary>The clean (ad-free) master playlist on Kaltura's own CDN, under Reshet 13's video
    /// partner 2748741 — what the app itself builds, rather than the ad-stitched
    /// <c>g-mana.live</c> URL the media file names.</summary>
    private const string CleanManifestUrlTemplate =
        "https://cdnapisec.kaltura.com/p/2748741/sp/274874100/playManifest/entryId/{0}/format/applehttp/protocol/https/a.m3u8";

    /// <summary>What a URL that fails to parse becomes in <see cref="RedactedUrl"/> — building a safe message must never itself throw.</summary>
    private const string UnparseableUrlPlaceholder = "<unparseable url>";

    public static string Service(string service, string action) =>
        string.Format(CultureInfo.InvariantCulture, KalturaServiceUrlTemplate, service, action);

    public static string CleanManifest(string entryId) =>
        string.Format(CultureInfo.InvariantCulture, CleanManifestUrlTemplate, Uri.EscapeDataString(entryId));

    /// <summary>
    /// N3: a URL reduced to its host and path, without its query string — the variant URLs inside
    /// a manifest are CloudFront-signed. Kaltura's session token travels in the request body, never
    /// in a URL, so nothing else needs scrubbing. Same rule as <c>MakoUrls.RedactedUrl</c>, kept
    /// here so neither provider reaches into the other.
    /// </summary>
    public static string RedactedUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            ? $"{uri.Host}{uri.AbsolutePath}"
            : UnparseableUrlPlaceholder;
}
