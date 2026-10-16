using Jellyfin.Plugin.IlVod.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IlVod.Providers.Kan.Source;

/// <summary>
/// The only class that speaks HTTP to Kan's hosts. Kan needs no headers, cookies or tokens, and
/// <c>mobapi.kan.org.il</c> shows no Cloudflare challenge — were one to appear, it answers 403 and is
/// unavailable like any other refusal. Redirects (the episode page's, Redge's to an edge node) are
/// the <c>HttpClient</c>'s to follow. Never logs or throws a query string (N3). Built over an
/// <c>HttpClient</c> the caller supplies, with <see cref="Timeout"/> and decompression set on it —
/// the lobby is 2.2 MB, 126 KB gzipped.
/// </summary>
public sealed class KanHttpClient : IKanHttp
{
    /// <summary>The timeout the DI wiring sets on the production <c>HttpClient</c> it builds.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;
    private readonly ILogger<KanHttpClient> _logger;

    public KanHttpClient(HttpClient http, ILogger<KanHttpClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<string> GetTextAsync(string url, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        string redactedUrl = SafeUrl.Redacted(url);
        _logger.LogInformation("kan: request GET {HostAndPath}", redactedUrl);

        using HttpResponseMessage response = await SendOrThrowAsync(request, redactedUrl, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new KanUnavailableException($"HTTP {(int)response.StatusCode} for {redactedUrl}");
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The transport's own exception is attached for its diagnostic value; Kan's URLs
    /// carry no secret, so it is passed on as is.</summary>
    private async Task<HttpResponseMessage> SendOrThrowAsync(HttpRequestMessage request, string redactedUrl, CancellationToken cancellationToken)
    {
        try
        {
            return await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new KanUnavailableException($"request failed for {redactedUrl}", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new KanUnavailableException($"request timed out for {redactedUrl}", exception);
        }
    }
}
