using System.Globalization;

namespace Jellyfin.Plugin.Mako.MakoApi;

/// <summary>
/// Builds the URLs mako's own Android app builds: filling placeholders into templates the app
/// itself fetches from its config endpoints, plus the two hosts step B's pacing and bot-challenge
/// handling care about.
/// </summary>
public static class MakoUrls
{
    /// <summary>The host a redirect to lands on when mako's bot challenge intervenes.</summary>
    public const string BotChallengeHost = "validate.perfdrive.com";

    /// <summary>The host every ordinary browse/page request goes to.</summary>
    public const string BrowseHost = "www.mako.co.il";

    private const string MainConfigUrlTemplate = "https://apps.mako.co.il/mobile/config/12Plus/android/mobile/{0}/mainConfig.json";
    private const string PageUrlPlaceholder = "%PAGE_URL%";
    private const string VcmIdPlaceholder = "%VCM_ID%";
    private const string ChannelIdPlaceholder = "%CHANNEL_ID%";
    private const string GalleryIdPlaceholder = "%GALLERY_ID%";
    private const string ConsumerPlaceholder = "%CONSUMER%";
    private const string PlayerConsumer = "mobileApp";

    /// <summary>What a URL that fails to parse becomes in <see cref="RedactedUrl"/> — building a safe message must never itself throw.</summary>
    private const string UnparseableUrlPlaceholder = "<unparseable url>";

    /// <summary>
    /// The one place that implements N3 ("never a query string in a log line or exception
    /// message"): reduces a URL to its host and path, dropping the query string and any fragment.
    /// A URL that fails to parse never throws — it becomes <see cref="UnparseableUrlPlaceholder"/>
    /// instead, since a helper whose whole job is building a safe error message must not add a new
    /// way to fail while doing it.
    /// </summary>
    public static string RedactedUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            ? $"{uri.Host}{uri.AbsolutePath}"
            : UnparseableUrlPlaceholder;

    public static string MainConfigUrl(string appVersion) =>
        string.Format(CultureInfo.InvariantCulture, MainConfigUrlTemplate, appVersion);

    /// <summary>
    /// Fills <paramref name="pagePath"/> into the base URL template. When the resulting URL has
    /// no query string of its own, the template's leading <c>&amp;</c> (meant to join onto one)
    /// becomes a <c>?</c> instead, matching the app's own <c>Utils</c> logic exactly.
    /// </summary>
    public static string Page(string baseUrlTemplate, string pagePath)
    {
        string url = baseUrlTemplate.Replace(PageUrlPlaceholder, pagePath);
        return url.Contains('?') ? url : ReplaceFirstAmpersandWithQuestionMark(url);
    }

    public static string Playlist(string playlistUrlTemplate, string vcmId, string channelId, string galleryChannelId) =>
        playlistUrlTemplate
            .Replace(VcmIdPlaceholder, vcmId)
            .Replace(ChannelIdPlaceholder, channelId)
            .Replace(GalleryIdPlaceholder, galleryChannelId)
            .Replace(ConsumerPlaceholder, PlayerConsumer);

    private static string ReplaceFirstAmpersandWithQuestionMark(string url)
    {
        int ampersandIndex = url.IndexOf('&');
        return ampersandIndex < 0
            ? url
            : string.Concat(url.AsSpan(0, ampersandIndex), "?", url.AsSpan(ampersandIndex + 1));
    }
}
