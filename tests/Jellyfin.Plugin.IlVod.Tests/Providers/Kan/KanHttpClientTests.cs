using System.Net;
using Jellyfin.Plugin.IlVod.Providers.Kan.Source;
using Jellyfin.Plugin.IlVod.Tests.Support;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Kan;

public class KanHttpClientTests
{
    private const string FeedUrl = "https://mobapi.kan.org.il/api/mobile/program?id=11510";

    [Fact]
    public async Task GetsTextAndLogsTheRequestWithoutItsQuery()
    {
        StubHttpHandler handler = new();
        handler.Enqueue(Response(HttpStatusCode.OK, "{\"title\":\"x\"}"));
        RecordingLogger<KanHttpClient> logger = new();
        KanHttpClient client = new(new HttpClient(handler), logger);

        string text = await client.GetTextAsync(FeedUrl, CancellationToken.None);

        Assert.Equal(HttpMethod.Get, Assert.Single(handler.Requests).Method);
        Assert.Equal("{\"title\":\"x\"}", text);
        Assert.Equal("kan: request GET mobapi.kan.org.il/api/mobile/program", Assert.Single(logger.Lines));
    }

    [Fact]
    public async Task AChallengeIsUnavailableNamingTheStatusAndOnlyTheHostAndPath()
    {
        StubHttpHandler handler = new();
        handler.Enqueue(Response(HttpStatusCode.Forbidden, "Just a moment..."));
        KanHttpClient client = CreateClient(handler);

        KanUnavailableException exception = await Assert.ThrowsAsync<KanUnavailableException>(
            () => client.GetTextAsync(FeedUrl, CancellationToken.None));

        Assert.Equal("HTTP 403 for mobapi.kan.org.il/api/mobile/program", exception.Message);
    }

    [Fact]
    public async Task ANetworkFailureIsUnavailable()
    {
        StubHttpHandler handler = new();
        handler.EnqueueException(new HttpRequestException("connection refused"));
        KanHttpClient client = CreateClient(handler);

        await Assert.ThrowsAsync<KanUnavailableException>(() => client.GetTextAsync(FeedUrl, CancellationToken.None));
    }

    [Fact]
    public async Task ATimeoutIsUnavailable()
    {
        StubHttpHandler handler = new();
        handler.EnqueueException(new TaskCanceledException("timed out"));
        KanHttpClient client = CreateClient(handler);

        KanUnavailableException exception = await Assert.ThrowsAsync<KanUnavailableException>(
            () => client.GetTextAsync(FeedUrl, CancellationToken.None));

        Assert.Contains("timed out", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACallerCancellingIsNotUnavailable()
    {
        StubHttpHandler handler = new();
        handler.EnqueueException(new TaskCanceledException("cancelled"));
        KanHttpClient client = CreateClient(handler);
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetTextAsync(FeedUrl, cancelled.Token));
    }

    private static KanHttpClient CreateClient(StubHttpHandler handler) =>
        new(new HttpClient(handler), new RecordingLogger<KanHttpClient>());

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };
}
