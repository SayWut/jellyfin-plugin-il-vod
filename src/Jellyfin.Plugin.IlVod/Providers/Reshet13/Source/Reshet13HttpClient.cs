using System.Text;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IlVod.Providers.Reshet13.Source;

/// <summary>
/// The only class that speaks HTTP to Reshet 13's hosts. Kaltura needs no special headers, shows no
/// bot protection and was never seen to rate-limit, so unlike <c>MakoHttpClient</c> there is no
/// pacing or redirect handling here — only turning every failure into
/// <see cref="Reshet13UnavailableException"/>. Never logs or throws a query string (N3). Built over
/// an <c>HttpClient</c> the caller supplies, with <see cref="Timeout"/> set on it.
/// </summary>
public sealed class Reshet13HttpClient : IReshet13Http
{
    /// <summary>The timeout the DI wiring sets on the production <c>HttpClient</c> it builds.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private const string JsonMediaType = "application/json";

    private readonly HttpClient _http;
    private readonly ILogger<Reshet13HttpClient> _logger;

    public Reshet13HttpClient(HttpClient http, ILogger<Reshet13HttpClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public Task<string> PostJsonAsync(string url, string body, CancellationToken cancellationToken) =>
        SendAsync(() => CreatePostRequest(url, body), cancellationToken);

    public Task<string> GetTextAsync(string url, CancellationToken cancellationToken) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);

    private static HttpRequestMessage CreatePostRequest(string url, string body) =>
        new(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8, JsonMediaType) };

    private async Task<string> SendAsync(Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = createRequest();
        string redactedUrl = Reshet13Urls.RedactedUrl(request.RequestUri!.ToString());
        _logger.LogInformation("reshet13: request {Method} {HostAndPath}", request.Method, redactedUrl);

        using HttpResponseMessage response = await SendOrThrowAsync(request, redactedUrl, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new Reshet13UnavailableException($"HTTP {(int)response.StatusCode} for {redactedUrl}");
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The transport's own exception is attached for its diagnostic value. Unlike mako's, Reshet
    /// 13's request URLs carry no secret — the session token is in the body — so it is passed on
    /// as is.
    /// </summary>
    private async Task<HttpResponseMessage> SendOrThrowAsync(HttpRequestMessage request, string redactedUrl, CancellationToken cancellationToken)
    {
        try
        {
            return await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new Reshet13UnavailableException($"request failed for {redactedUrl}", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new Reshet13UnavailableException($"request timed out for {redactedUrl}", exception);
        }
    }
}
