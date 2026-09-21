using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Mako.MakoApi;

/// <summary>
/// The only class that speaks HTTP to mako: adds the app's headers to every request, paces and
/// backs off traffic to <see cref="MakoUrls.BrowseHost"/> through a <see cref="RequestPacer"/>,
/// follows redirects by hand so a bot challenge can be told apart from an ordinary one, and turns
/// failures into the plugin's own typed exceptions. Never logs or throws a query string (N3).
/// Built over an <c>HttpClient</c> the caller supplies — the production client's own construction
/// (<c>AllowAutoRedirect = false</c>, its message handler) is task B10's DI wiring, not this class.
/// </summary>
public sealed class MakoHttpClient : IMakoHttp
{
    public const string UserAgent =
        "Mozilla/5.0 (Linux; Android 13; Pixel 6) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Mobile Safari/537.36";

    public const string AcceptLanguage = "he-IL,he;q=0.9,en;q=0.8";

    /// <summary>The timeout task B10 sets on the production <c>HttpClient</c> it builds.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private const int MaxRedirectHops = 5;

    private readonly HttpClient _http;
    private readonly RequestPacer _pacer;
    private readonly ILogger<MakoHttpClient> _logger;

    public MakoHttpClient(HttpClient http, RequestPacer pacer, ILogger<MakoHttpClient> logger)
    {
        _http = http;
        _pacer = pacer;
        _logger = logger;
    }

    public Task<string> GetTextAsync(string url, CancellationToken cancellationToken) =>
        SendAsync(url, hopUrl => new HttpRequestMessage(HttpMethod.Get, hopUrl), cancellationToken);

    public Task<string> PostTextAsync(string url, string body, string contentType, CancellationToken cancellationToken) =>
        SendAsync(url, hopUrl => CreatePostRequest(hopUrl, body, contentType), cancellationToken);

    private static HttpRequestMessage CreatePostRequest(string url, string body, string contentType)
    {
        HttpRequestMessage request = new(HttpMethod.Post, url)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body))
        };
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return request;
    }

    private async Task<string> SendAsync(string url, Func<string, HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        if (!IsBrowseHost(url))
        {
            return await SendWithRedirectsAsync(requestFactory, url, cancellationToken).ConfigureAwait(false);
        }

        using IDisposable turn = await EnterPacedTurnOrThrowAsync(url, cancellationToken).ConfigureAwait(false);
        return await SendWithRedirectsAsync(requestFactory, url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A cheap pre-check refuses outright when already backing off, without even joining the
    /// pacer's queue. But the pacer decides again for itself once it grants exclusive access
    /// (<see cref="RequestPacer.EnterAsync"/>) — a caller can start waiting before a concurrent
    /// request starts the back-off, and the pre-check alone would let it through right after.
    /// </summary>
    private async Task<IDisposable> EnterPacedTurnOrThrowAsync(string url, CancellationToken cancellationToken)
    {
        if (_pacer.IsBackingOff)
        {
            throw new BotChallengeException(url);
        }

        try
        {
            return await _pacer.EnterAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (BackOffRefusalException)
        {
            throw new BotChallengeException(url);
        }
    }

    private async Task<string> SendWithRedirectsAsync(Func<string, HttpRequestMessage> requestFactory, string url, CancellationToken cancellationToken)
    {
        string currentUrl = url;
        for (int hop = 0; hop <= MaxRedirectHops; hop++)
        {
            using HttpResponseMessage response = await SendOnceAsync(requestFactory, currentUrl, cancellationToken).ConfigureAwait(false);

            if (!IsRedirect(response.StatusCode))
            {
                return await ReadSuccessOrThrowAsync(response, currentUrl, cancellationToken).ConfigureAwait(false);
            }

            currentUrl = ResolveRedirectOrThrow(response, currentUrl);
        }

        throw new MakoRequestException($"too many redirects for {MakoUrls.RedactedUrl(currentUrl)}");
    }

    private async Task<HttpResponseMessage> SendOnceAsync(Func<string, HttpRequestMessage> requestFactory, string url, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = requestFactory(url);
        AddAppHeaders(request);
        LogRequest(request.Method, url);

        try
        {
            return await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new MakoRequestException($"request failed for {MakoUrls.RedactedUrl(url)}", RedactIfItNamesTheUrl(exception, url));
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MakoRequestException($"request timed out for {MakoUrls.RedactedUrl(url)}", RedactIfItNamesTheUrl(exception, url));
        }
    }

    /// <summary>
    /// The inner exception a transport failure carries is attached for its diagnostic value, but
    /// nothing guarantees its own message never quotes the URL it was trying to reach — including
    /// the query string (N3). If it does, it is replaced with a copy whose message has the raw URL
    /// swapped for its redacted form, rather than passed through unexamined.
    /// </summary>
    private static Exception RedactIfItNamesTheUrl(Exception exception, string url) =>
        exception.Message.Contains(url, StringComparison.Ordinal)
            ? new MakoRequestException(exception.Message.Replace(url, MakoUrls.RedactedUrl(url), StringComparison.Ordinal))
            : exception;

    private static void AddAppHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept-Language", AcceptLanguage);
    }

    private static async Task<string> ReadSuccessOrThrowAsync(HttpResponseMessage response, string url, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new MakoRequestException($"HTTP {(int)response.StatusCode} for {MakoUrls.RedactedUrl(url)}");
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private string ResolveRedirectOrThrow(HttpResponseMessage response, string currentUrl)
    {
        Uri? location = response.Headers.Location;
        if (location is null)
        {
            throw new MakoRequestException($"redirect without a Location header for {MakoUrls.RedactedUrl(currentUrl)}");
        }

        Uri resolved = location.IsAbsoluteUri ? location : new Uri(new Uri(currentUrl), location);

        if (resolved.Host.Contains(MakoUrls.BotChallengeHost, StringComparison.OrdinalIgnoreCase))
        {
            _pacer.BackOff();
            throw new BotChallengeException(currentUrl);
        }

        return resolved.ToString();
    }

    private void LogRequest(HttpMethod method, string url) =>
        _logger.LogInformation("mako: request {Method} {HostAndPath}", method, MakoUrls.RedactedUrl(url));

    private static bool IsBrowseHost(string url) => new Uri(url).Host == MakoUrls.BrowseHost;

    private static bool IsRedirect(HttpStatusCode statusCode) => (int)statusCode is >= 300 and < 400;
}
