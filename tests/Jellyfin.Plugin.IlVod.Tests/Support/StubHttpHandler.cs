using System.Net.Http.Headers;

namespace Jellyfin.Plugin.IlVod.Tests.Support;

/// <summary>
/// A snapshot of one request <see cref="StubHttpHandler"/> received, taken before the response is
/// handed back so it stays valid for assertions after the real <c>HttpRequestMessage</c> — and its
/// content — are disposed.
/// </summary>
public sealed record CapturedRequest(
    HttpMethod Method,
    Uri? RequestUri,
    string? UserAgent,
    string? AcceptLanguage,
    string? Accept,
    string? ContentType,
    string? Body);

/// <summary>
/// A hand-written <c>HttpMessageHandler</c> that returns pre-queued responses and records what it
/// was asked to send, so <c>MakoHttpClient</c> can be tested without a real network (matching this
/// suite's no-mocking-library convention).
/// </summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();
    private readonly List<CapturedRequest> _requests = [];

    public IReadOnlyList<CapturedRequest> Requests => _requests;

    public void Enqueue(HttpResponseMessage response) => _responses.Enqueue(() => response);

    /// <summary>Makes the next request throw <paramref name="exception"/> instead of getting a response — simulating a transport failure.</summary>
    public void EnqueueException(Exception exception) => _responses.Enqueue(() => throw exception);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Add(await CaptureAsync(request, cancellationToken).ConfigureAwait(false));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("StubHttpHandler has no queued response left for this request.");
        }

        return _responses.Dequeue()();
    }

    private static async Task<CapturedRequest> CaptureAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return new CapturedRequest(
            request.Method,
            request.RequestUri,
            HeaderValue(request.Headers, "User-Agent"),
            HeaderValue(request.Headers, "Accept-Language"),
            HeaderValue(request.Headers, "Accept"),
            request.Content?.Headers.ContentType?.ToString(),
            body);
    }

    /// <summary>
    /// The header's exact raw value, via <c>NonValidated</c> so a well-known header name (such as
    /// <c>User-Agent</c>) is not re-parsed into tokens and rejoined differently from what was sent.
    /// </summary>
    private static string? HeaderValue(HttpHeaders headers, string name)
    {
        // The indexer throws for a header that was never sent; an absent header reads as null.
        return headers.NonValidated.TryGetValues(name, out HeaderStringValues values) && values.Count > 0
            ? string.Join(", ", values)
            : null;
    }
}
