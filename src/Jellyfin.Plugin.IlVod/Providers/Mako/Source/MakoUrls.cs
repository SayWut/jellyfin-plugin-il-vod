using System.Globalization;

namespace Jellyfin.Plugin.IlVod.Providers.Mako.Source;

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

    /// <summary>Never names the offending path: the message reaches a log line and a user-facing warning.</summary>
    private const string NotAMakoPagePathMessage = "not a mako page path";

    /// <summary>A minimal, unquestionably safe page path, used only to read a template's own host.</summary>
    private const string HostProbePagePath = "/";

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
    /// <exception cref="ArgumentException">
    /// <paramref name="pagePath"/> is not a mako page path. Show and season ids reach here from a
    /// folder id a client sent, so the path is untrusted: <c>@evil.example/x</c> filled into the
    /// template would produce <c>https://www.mako.co.il@evil.example/x</c>, a request to
    /// <c>evil.example</c> that N4's pacing and bot-challenge back-off would never see.
    /// </exception>
    public static string Page(string baseUrlTemplate, string pagePath)
    {
        if (!IsPagePathThatCannotChangeHost(pagePath))
        {
            throw new ArgumentException(NotAMakoPagePathMessage, nameof(pagePath));
        }

        string url = baseUrlTemplate.Replace(PageUrlPlaceholder, pagePath);
        url = url.Contains('?') ? url : ReplaceFirstAmpersandWithQuestionMark(url);

        if (!StaysOnTheTemplatesBrowseHost(baseUrlTemplate, url))
        {
            throw new ArgumentException(NotAMakoPagePathMessage, nameof(pagePath));
        }

        return url;
    }

    public static string Playlist(string playlistUrlTemplate, string vcmId, string channelId, string galleryChannelId) =>
        playlistUrlTemplate
            .Replace(VcmIdPlaceholder, vcmId)
            .Replace(ChannelIdPlaceholder, channelId)
            .Replace(GalleryIdPlaceholder, galleryChannelId)
            .Replace(ConsumerPlaceholder, PlayerConsumer);

    /// <summary>
    /// A page path is an absolute path and nothing else: it starts with a single <c>/</c> and
    /// carries none of the characters that let a path reinterpret the authority it is appended
    /// to — a userinfo <c>@</c>, a protocol-relative <c>//</c>, a scheme <c>://</c>, a backslash
    /// (which several parsers treat as <c>/</c>), or whitespace.
    /// </summary>
    private static bool IsPagePathThatCannotChangeHost(string pagePath) =>
        pagePath.StartsWith('/')
        && !pagePath.StartsWith("//", StringComparison.Ordinal)
        && !pagePath.Contains('@', StringComparison.Ordinal)
        && !pagePath.Contains('\\', StringComparison.Ordinal)
        && !pagePath.Contains("://", StringComparison.Ordinal)
        && !pagePath.Any(char.IsWhiteSpace);

    /// <summary>
    /// Defence in depth behind <see cref="IsPagePathThatCannotChangeHost"/>: whatever the filled
    /// URL turned out to be, a template aimed at <see cref="BrowseHost"/> must still request
    /// <see cref="BrowseHost"/>. A template aimed anywhere else is left to its own host.
    /// </summary>
    private static bool StaysOnTheTemplatesBrowseHost(string baseUrlTemplate, string url) =>
        HostOf(baseUrlTemplate.Replace(PageUrlPlaceholder, HostProbePagePath)) != BrowseHost || HostOf(url) == BrowseHost;

    /// <summary>The host of <paramref name="url"/>, or null when it does not parse at all.</summary>
    private static string? HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? uri.Host : null;

    private static string ReplaceFirstAmpersandWithQuestionMark(string url)
    {
        int ampersandIndex = url.IndexOf('&');
        return ampersandIndex < 0
            ? url
            : string.Concat(url.AsSpan(0, ampersandIndex), "?", url.AsSpan(ampersandIndex + 1));
    }
}
