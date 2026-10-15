using System.Net;
using Jellyfin.Plugin.IlVod.MakoApi;
using Jellyfin.Plugin.IlVod.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.IlVod.Tests.Mako;

public class MakoHttpClientTests
{
    private const string BrowsePageUrl = "https://www.mako.co.il/mako-vod-keshet/shemesh?platform=mobileApp";
    private const string ConfigUrl = "https://apps.mako.co.il/mobile/config/12Plus/android/mobile/43.0/mainConfig.json";

    [Fact]
    public async Task SendsTheAppLikeHeaders()
    {
        StubHttpHandler handler = new();
        handler.Enqueue(OkResponse("{}"));
        MakoHttpClient client = CreateClient(handler);

        await client.GetTextAsync(ConfigUrl, CancellationToken.None);

        CapturedRequest request = Assert.Single(handler.Requests);
        Assert.Equal(MakoHttpClient.UserAgent, request.UserAgent);
        Assert.Equal(MakoHttpClient.AcceptLanguage, request.AcceptLanguage);
        Assert.Equal(MakoHttpClient.Accept, request.Accept);
    }

    [Fact]
    public async Task ARedirectToTheBotChallengeHostIsABotChallengeAndStartsTheBackOff()
    {
        StubHttpHandler handler = new();
        handler.Enqueue(RedirectResponse("https://validate.perfdrive.com/challenge"));
        RequestPacer pacer = CreatePacer();
        MakoHttpClient client = CreateClient(handler, pacer);

        await Assert.ThrowsAsync<BotChallengeException>(
            () => client.GetTextAsync(BrowsePageUrl, CancellationToken.None));

        Assert.True(pacer.IsBackingOff);
    }

    [Fact]
    public async Task WhileBackingOffBrowseRequestsAreRefusedWithoutTouchingTheNetwork()
    {
        StubHttpHandler handler = new();
        RequestPacer pacer = CreatePacer();
        pacer.BackOff();
        MakoHttpClient client = CreateClient(handler, pacer);

        await Assert.ThrowsAsync<BotChallengeException>(
            () => client.GetTextAsync(BrowsePageUrl, CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ARequestQueuedBehindABackOffThatStartsWhileWaitingIsRefusedWithoutTouchingTheNetwork()
    {
        StubHttpHandler handler = new();
        RequestPacer pacer = CreatePacer();
        MakoHttpClient client = CreateClient(handler, pacer);

        // "A" holds the pacer's one turn directly, so "B" (through the client) must queue behind
        // it. B's own pre-check sees no back-off yet — it only starts once B is already waiting.
        IDisposable heldTurn = await pacer.EnterAsync(CancellationToken.None);
        Task<string> queuedRequest = client.GetTextAsync(BrowsePageUrl, CancellationToken.None);

        pacer.BackOff();
        heldTurn.Dispose();

        await Assert.ThrowsAsync<BotChallengeException>(() => queuedRequest);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AProtocolRelativeRedirectToTheBotChallengeHostIsDetected()
    {
        StubHttpHandler handler = new();
        handler.Enqueue(RedirectResponse("//validate.perfdrive.com/challenge"));
        RequestPacer pacer = CreatePacer();
        MakoHttpClient client = CreateClient(handler, pacer);

        await Assert.ThrowsAsync<BotChallengeException>(
            () => client.GetTextAsync(BrowsePageUrl, CancellationToken.None));

        Assert.True(pacer.IsBackingOff);
    }

    [Fact]
    public async Task ADifferentlyCasedBotChallengeHostIsDetected()
    {
        StubHttpHandler handler = new();
        handler.Enqueue(RedirectResponse("https://VALIDATE.PERFDRIVE.COM/challenge"));
        RequestPacer pacer = CreatePacer();
        MakoHttpClient client = CreateClient(handler, pacer);

        await Assert.ThrowsAsync<BotChallengeException>(
            () => client.GetTextAsync(BrowsePageUrl, CancellationToken.None));

        Assert.True(pacer.IsBackingOff);
    }

    [Fact]
    public async Task RequestsToOtherHostsAreNotPacedOrRefused()
    {
        StubHttpHandler handler = new();
        handler.Enqueue(OkResponse("{}"));
        RequestPacer pacer = CreatePacer();
        pacer.BackOff();
        MakoHttpClient client = CreateClient(handler, pacer);

        string body = await client.GetTextAsync(ConfigUrl, CancellationToken.None);

        Assert.Equal("{}", body);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task AnOrdinaryRedirectIsFollowed()
    {
        StubHttpHandler handler = new();
        handler.Enqueue(RedirectResponse("/mako-vod-keshet/shemesh-new?platform=mobileApp"));
        handler.Enqueue(OkResponse("moved-body"));
        MakoHttpClient client = CreateClient(handler);

        string body = await client.GetTextAsync(BrowsePageUrl, CancellationToken.None);

        Assert.Equal("moved-body", body);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(
            new Uri("https://www.mako.co.il/mako-vod-keshet/shemesh-new?platform=mobileApp"),
            handler.Requests[1].RequestUri);
    }

    [Fact]
    public async Task AnErrorStatusIsAMakoRequestExceptionThatNamesHostAndPathButNotTheQuery()
    {
        StubHttpHandler handler = new();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        MakoHttpClient client = CreateClient(handler);

        MakoRequestException exception = await Assert.ThrowsAsync<MakoRequestException>(
            () => client.GetTextAsync(BrowsePageUrl, CancellationToken.None));

        Assert.Contains("www.mako.co.il/mako-vod-keshet/shemesh", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("platform=mobileApp", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain('?', exception.Message);
    }

    [Fact]
    public async Task TooManyRedirectsNamesTheLastHopNotTheFirst()
    {
        StubHttpHandler handler = new();
        for (int hopNumber = 1; hopNumber <= 6; hopNumber++)
        {
            handler.Enqueue(RedirectResponse($"https://www.mako.co.il/mako-vod-keshet/hop-{hopNumber}"));
        }

        MakoHttpClient client = CreateClient(handler);

        MakoRequestException exception = await Assert.ThrowsAsync<MakoRequestException>(
            () => client.GetTextAsync(BrowsePageUrl, CancellationToken.None));

        Assert.Equal(6, handler.Requests.Count);
        Assert.Contains("hop-6", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("mako-vod-keshet/shemesh", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATransportFailureNeverNamesTheQueryEvenInTheInnerException()
    {
        const string UrlWithMarker = "https://www.mako.co.il/mako-vod-keshet/shemesh?hdnea=SECRET";
        StubHttpHandler handler = new();
        handler.EnqueueException(new HttpRequestException($"connection reset while calling {UrlWithMarker}"));
        MakoHttpClient client = CreateClient(handler);

        MakoRequestException exception = await Assert.ThrowsAsync<MakoRequestException>(
            () => client.GetTextAsync(UrlWithMarker, CancellationToken.None));

        Assert.DoesNotContain("hdnea=SECRET", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingLocationHeaderNeverNamesTheQuery()
    {
        const string UrlWithMarker = "https://www.mako.co.il/mako-vod-keshet/shemesh?hdnea=SECRET";
        StubHttpHandler handler = new();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.Found));
        MakoHttpClient client = CreateClient(handler);

        MakoRequestException exception = await Assert.ThrowsAsync<MakoRequestException>(
            () => client.GetTextAsync(UrlWithMarker, CancellationToken.None));

        Assert.DoesNotContain("hdnea=SECRET", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TooManyRedirectsNeverNamesTheQuery()
    {
        const string UrlWithMarker = "https://www.mako.co.il/mako-vod-keshet/shemesh?hdnea=SECRET";
        StubHttpHandler handler = new();
        for (int hopNumber = 1; hopNumber <= 6; hopNumber++)
        {
            handler.Enqueue(RedirectResponse($"https://www.mako.co.il/mako-vod-keshet/hop-{hopNumber}?hdnea=SECRET"));
        }

        MakoHttpClient client = CreateClient(handler);

        MakoRequestException exception = await Assert.ThrowsAsync<MakoRequestException>(
            () => client.GetTextAsync(UrlWithMarker, CancellationToken.None));

        Assert.DoesNotContain("hdnea=SECRET", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostSendsTheBodyWithItsContentType()
    {
        StubHttpHandler handler = new();
        handler.Enqueue(OkResponse("token-body"));
        MakoHttpClient client = CreateClient(handler);

        string body = await client.PostTextAsync(
            "https://mass.mako.co.il/ClicksStatistics/entitlementsServicesV2.jsp?et=egt",
            "encrypted-payload",
            "application/json; charset=utf-8",
            CancellationToken.None);

        Assert.Equal("token-body", body);
        CapturedRequest request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("encrypted-payload", request.Body);
        Assert.Equal("application/json; charset=utf-8", request.ContentType);
    }

    private static MakoHttpClient CreateClient(StubHttpHandler handler) => CreateClient(handler, CreatePacer());

    private static MakoHttpClient CreateClient(StubHttpHandler handler, RequestPacer pacer) =>
        new(new HttpClient(handler), pacer, NullLogger<MakoHttpClient>.Instance);

    private static RequestPacer CreatePacer() => new(new FakeClock(), (_, _) => Task.CompletedTask);

    private static HttpResponseMessage OkResponse(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static HttpResponseMessage RedirectResponse(string location)
    {
        HttpResponseMessage response = new(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }
}
