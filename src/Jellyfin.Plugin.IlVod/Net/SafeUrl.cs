namespace Jellyfin.Plugin.IlVod.Net;

/// <summary>
/// The one place that implements N3 ("never a query string in a log line or exception message"),
/// for every provider: mako's URLs carry an <c>hdnea</c> token and Kaltura's variant URLs are
/// CloudFront-signed, both in the query string. Neutral ground, so no provider reaches into another.
/// </summary>
public static class SafeUrl
{
    /// <summary>What a URL that fails to parse becomes — building a safe message must never itself throw.</summary>
    private const string UnparseableUrlPlaceholder = "<unparseable url>";

    /// <summary>
    /// <paramref name="url"/> reduced to its host and path, without its query string or fragment.
    /// A URL that fails to parse becomes <see cref="UnparseableUrlPlaceholder"/> rather than
    /// throwing, since a helper whose whole job is building a safe message must not add a new way
    /// to fail while doing it.
    /// </summary>
    public static string Redacted(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            ? $"{uri.Host}{uri.AbsolutePath}"
            : UnparseableUrlPlaceholder;
}
