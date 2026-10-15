using System.Net;
using Jellyfin.Plugin.IlVod.Providers.Reshet13.Source;
using Jellyfin.Plugin.IlVod.Tests.Support;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Reshet13;

public class Reshet13HttpClientTests
{
    private const string ListUrl = "https://5031.frp1.ott.kaltura.com/api_v3/service/asset/action/list";
    private const string SignedUrl = "https://cfvod.kaltura.com/a/index.m3u8?Policy=SECRET&Signature=SECRET";

    [Fact]
    public async Task PostsTheBodyAsJsonAndAnswersTheResponseText()
    {
        StubHttpHandler handler = new();
        handler.Enqueue(Response(HttpStatusCode.OK, "{\"result\":{}}"));
        Reshet13HttpClient client = CreateClient(handler);

        string answer = await client.PostJsonAsync(ListUrl, "{\"a\":1}", CancellationToken.None);

        CapturedRequest request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("{\"a\":1}", request.Body);
        Assert.StartsWith("application/json", request.ContentType, StringComparison.Ordinal);
        Assert.Equal("{\"result\":{}}", answer);
    }

    [Fact]
    public async Task GetsText()
    {
        StubHttpHandler handler = new();
        handler.Enqueue(Response(HttpStatusCode.OK, "#EXTM3U"));
        Reshet13HttpClient client = CreateClient(handler);

        string text = await client.GetTextAsync(SignedUrl, CancellationToken.None);

        Assert.Equal(HttpMethod.Get, Assert.Single(handler.Requests).Method);
        Assert.Equal("#EXTM3U", text);
    }

    [Fact]
    public async Task AFailedStatusIsUnavailableNamingOnlyTheHostAndPath()
    {
        StubHttpHandler handler = new();
        handler.Enqueue(Response(HttpStatusCode.ServiceUnavailable, "down"));
        Reshet13HttpClient client = CreateClient(handler);

        Reshet13UnavailableException exception = await Assert.ThrowsAsync<Reshet13UnavailableException>(
            () => client.GetTextAsync(SignedUrl, CancellationToken.None));

        Assert.Contains("503", exception.Message, StringComparison.Ordinal);
        Assert.Contains("cfvod.kaltura.com/a/index.m3u8", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANetworkFailureIsUnavailable()
    {
        StubHttpHandler handler = new();
        handler.EnqueueException(new HttpRequestException("connection refused"));
        Reshet13HttpClient client = CreateClient(handler);

        await Assert.ThrowsAsync<Reshet13UnavailableException>(() => client.PostJsonAsync(ListUrl, "{}", CancellationToken.None));
    }

    [Fact]
    public async Task ATimeoutIsUnavailable()
    {
        StubHttpHandler handler = new();
        handler.EnqueueException(new TaskCanceledException("timed out"));
        Reshet13HttpClient client = CreateClient(handler);

        Reshet13UnavailableException exception = await Assert.ThrowsAsync<Reshet13UnavailableException>(
            () => client.PostJsonAsync(ListUrl, "{}", CancellationToken.None));

        Assert.Contains("timed out", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACallerCancellingIsNotUnavailable()
    {
        StubHttpHandler handler = new();
        handler.EnqueueException(new TaskCanceledException("cancelled"));
        Reshet13HttpClient client = CreateClient(handler);
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.PostJsonAsync(ListUrl, "{}", cancelled.Token));
    }

    [Fact]
    public void RequestLogLinesNameOnlyTheHostAndPath()
    {
        Assert.Equal("cfvod.kaltura.com/a/index.m3u8", Reshet13Urls.RedactedUrl(SignedUrl));
    }

    private static Reshet13HttpClient CreateClient(StubHttpHandler handler) =>
        new(new HttpClient(handler), new RecordingLogger<Reshet13HttpClient>());

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };
}
