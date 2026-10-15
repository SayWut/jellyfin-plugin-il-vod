using System.Text.Json;
using Jellyfin.Plugin.IlVod.Providers.Reshet13.Source;
using Jellyfin.Plugin.IlVod.Tests.Support;
using static Jellyfin.Plugin.IlVod.Tests.Providers.Reshet13.Reshet13Fixtures;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Reshet13;

public class KalturaOttClientTests
{
    private const string BaseFields = "\"apiVersion\":\"6.5.0\",\"clientTag\":\"ReshetWeb\",\"partnerId\":5031";
    private const string FreshKs = "FRESH-KS";

    private static readonly string FreshLoginJson = LoginJson.Replace(Ks, FreshKs, StringComparison.Ordinal);

    [Fact]
    public async Task LogsInOnceAndReusesTheSessionForEveryCall()
    {
        FakeReshet13Http http = SeededHttp();
        KalturaOttClient client = CreateClient(http);

        await client.CallAsync(KalturaQueries.Asset(AssetGetId), CancellationToken.None);
        await client.CallAsync(KalturaQueries.Asset(AssetGetId), CancellationToken.None);

        Assert.Single(http.Posts, call => call.Url == LoginUrl);
        Assert.Equal(2, http.Posts.Count(call => call.Url == GetUrl));
    }

    [Fact]
    public async Task EveryBodyCarriesTheBaseFieldsAndEveryCallAfterLoginTheSession()
    {
        FakeReshet13Http http = SeededHttp();
        KalturaOttClient client = CreateClient(http);

        await client.CallAsync(KalturaQueries.Asset(AssetGetId), CancellationToken.None);

        Assert.All(http.Posts, call => Assert.StartsWith("{" + BaseFields, call.Body, StringComparison.Ordinal));
        Assert.Equal("{" + BaseFields + "}", http.Posts[0].Body);
        Assert.Contains($"\"ks\":\"{Ks}\"", http.Posts[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IdenticalCallsSendByteIdenticalBodies()
    {
        FakeReshet13Http http = SeededHttp();
        KalturaOttClient client = CreateClient(http);

        await client.CallAsync(KalturaQueries.EpisodesPage(SeriesId, 1), CancellationToken.None);
        await client.CallAsync(KalturaQueries.EpisodesPage(SeriesId, 1), CancellationToken.None);

        Reshet13Call[] episodeCalls = [.. http.Posts.Where(call => call.Url == ListUrl)];
        Assert.Equal(2, episodeCalls.Length);
        Assert.Equal(episodeCalls[0].Body, episodeCalls[1].Body);
    }

    [Fact]
    public async Task TheResultIsUnwrappedFromKalturasEnvelope()
    {
        KalturaOttClient client = CreateClient(SeededHttp());

        JsonElement result = await client.CallAsync(KalturaQueries.Asset(AssetGetId), CancellationToken.None);

        Assert.Equal(2361602, result.GetProperty("id").GetInt32());
    }

    [Theory]
    [InlineData("500004")]
    [InlineData("500015")]
    [InlineData("500016")]
    public async Task ASessionErrorLogsInAgainAndRetriesOnce(string sessionErrorCode)
    {
        FakeReshet13Http http = new();
        http.RespondToPost(LoginUrl, Any, LoginJson, FreshLoginJson);
        http.RespondToPost(GetUrl, IsAssetGet, SessionError(sessionErrorCode), AssetGetJson);
        KalturaOttClient client = CreateClient(http);

        JsonElement result = await client.CallAsync(KalturaQueries.Asset(AssetGetId), CancellationToken.None);

        Assert.Equal(2361602, result.GetProperty("id").GetInt32());
        Assert.Equal(2, http.Posts.Count(call => call.Url == LoginUrl));
        Reshet13Call retry = http.Posts.Last(call => call.Url == GetUrl);
        Assert.Contains($"\"ks\":\"{FreshKs}\"", retry.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASecondSessionErrorAfterTheRetryIsUnavailableWithoutLoggingInAThirdTime()
    {
        FakeReshet13Http http = new();
        http.RespondToPost(LoginUrl, Any, LoginJson, FreshLoginJson);
        http.RespondToPost(GetUrl, IsAssetGet, Error500015Json);
        KalturaOttClient client = CreateClient(http);

        Reshet13UnavailableException exception = await Assert.ThrowsAsync<Reshet13UnavailableException>(
            () => client.CallAsync(KalturaQueries.Asset(AssetGetId), CancellationToken.None));

        Assert.Contains("500015", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, http.Posts.Count(call => call.Url == LoginUrl));
        Assert.Equal(2, http.Posts.Count(call => call.Url == GetUrl));
    }

    [Fact]
    public async Task AnotherErrorCodeIsUnavailableNamingTheCodeTheCallAndItsSubjectButNeverTheSession()
    {
        // 500007 is "Asset not found": a 5000xx code, but not one a new session cures.
        FakeReshet13Http http = new();
        http.RespondToPost(LoginUrl, Any, LoginJson);
        http.RespondToPost(GetUrl, IsAssetGet, Error500015Json.Replace("500015", "500007", StringComparison.Ordinal));
        KalturaOttClient client = CreateClient(http);

        Reshet13UnavailableException exception = await Assert.ThrowsAsync<Reshet13UnavailableException>(
            () => client.CallAsync(KalturaQueries.Asset(AssetGetId), CancellationToken.None));

        Assert.Contains("500007", exception.Message, StringComparison.Ordinal);
        Assert.Contains("asset/get", exception.Message, StringComparison.Ordinal);
        Assert.Contains(AssetGetId, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Ks, exception.Message, StringComparison.Ordinal);
        Assert.Single(http.Posts, call => call.Url == LoginUrl);
    }

    [Fact]
    public async Task ALoginThatFailsIsUnavailableAndNeverAsksTheQuestion()
    {
        FakeReshet13Http http = new();
        http.RespondToPost(LoginUrl, Any, Error500015Json.Replace("500015", "1003", StringComparison.Ordinal));
        KalturaOttClient client = CreateClient(http);

        Reshet13UnavailableException exception = await Assert.ThrowsAsync<Reshet13UnavailableException>(
            () => client.CallAsync(KalturaQueries.Asset(AssetGetId), CancellationToken.None));

        Assert.Contains("anonymousLogin", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(http.Posts, call => call.Url == GetUrl);
    }

    [Fact]
    public async Task AnAnswerThatIsNotKalturasEnvelopeIsAFormatException()
    {
        FakeReshet13Http http = new();
        http.RespondToPost(LoginUrl, Any, LoginJson);
        http.RespondToPost(GetUrl, IsAssetGet, "<html>not kaltura</html>");
        KalturaOttClient client = CreateClient(http);

        await Assert.ThrowsAsync<FormatException>(
            () => client.CallAsync(KalturaQueries.Asset(AssetGetId), CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentFirstCallsShareOneLogin()
    {
        FakeReshet13Http http = SeededHttp();
        KalturaOttClient client = CreateClient(http);

        await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => client.CallAsync(KalturaQueries.Asset(AssetGetId), CancellationToken.None))));

        Assert.Single(http.Posts, call => call.Url == LoginUrl);
    }

    [Fact]
    public async Task ListingWalksThePagesUntilTheTotalCountIsReached()
    {
        FakeReshet13Http http = new();
        http.RespondToPost(LoginUrl, Any, LoginJson);
        http.RespondToPost(ListUrl, IsPage(1), ListAnswer(totalCount: 3, "{\"id\":1}", "{\"id\":2}"));
        http.RespondToPost(ListUrl, IsPage(2), ListAnswer(totalCount: 3, "{\"id\":3}"));
        KalturaOttClient client = CreateClient(http);

        IReadOnlyList<JsonElement> assets = await client.ListAllAsync(KalturaQueries.SeriesPage, CancellationToken.None);

        Assert.Equal([1, 2, 3], assets.Select(asset => asset.GetProperty("id").GetInt32()));
        Assert.Equal(2, http.Posts.Count(call => call.Url == ListUrl));
    }

    [Fact]
    public async Task AWalkThatEndsShortOfTheTotalCountIsAFormatExceptionNamingTheCountsNeverTheSession()
    {
        FakeReshet13Http http = new();
        http.RespondToPost(LoginUrl, Any, LoginJson);
        http.RespondToPost(ListUrl, IsPage(1), ListAnswer(totalCount: 5, "{\"id\":1}"));
        http.RespondToPost(ListUrl, IsPage(2), ListAnswer(totalCount: 5));
        KalturaOttClient client = CreateClient(http);

        FormatException exception = await Assert.ThrowsAsync<FormatException>(
            () => client.ListAllAsync(KalturaQueries.SeriesPage, CancellationToken.None));

        Assert.Contains("1 of 5", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Ks, exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, http.Posts.Count(call => call.Url == ListUrl));
    }

    [Fact]
    public async Task AnAssetRepeatedOnALaterPageIsListedOnce()
    {
        // Kaltura orders episodes by EpisodeNumber, which ties across seasons, so a tied asset can
        // land on both sides of a page boundary.
        FakeReshet13Http http = new();
        http.RespondToPost(LoginUrl, Any, LoginJson);
        http.RespondToPost(ListUrl, IsPage(1), ListAnswer(totalCount: 3, "{\"id\":1}", "{\"id\":2}"));
        http.RespondToPost(ListUrl, IsPage(2), ListAnswer(totalCount: 3, "{\"id\":2}", "{\"id\":3}"));
        KalturaOttClient client = CreateClient(http);

        IReadOnlyList<JsonElement> assets = await client.ListAllAsync(KalturaQueries.SeriesPage, CancellationToken.None);

        Assert.Equal([1, 2, 3], assets.Select(asset => asset.GetProperty("id").GetInt32()));
    }

    [Fact]
    public async Task ARepeatThatPushedAnAssetOffEveryPageIsAFormatException()
    {
        FakeReshet13Http http = new();
        http.RespondToPost(LoginUrl, Any, LoginJson);
        http.RespondToPost(ListUrl, IsPage(1), ListAnswer(totalCount: 3, "{\"id\":1}", "{\"id\":2}"));
        http.RespondToPost(ListUrl, IsPage(2), ListAnswer(totalCount: 3, "{\"id\":2}"));
        KalturaOttClient client = CreateClient(http);

        await Assert.ThrowsAsync<FormatException>(
            () => client.ListAllAsync(KalturaQueries.SeriesPage, CancellationToken.None));
    }

    [Fact]
    public async Task APageOfNothingButRepeatsEndsTheWalk()
    {
        FakeReshet13Http http = new();
        http.RespondToPost(LoginUrl, Any, LoginJson);
        http.RespondToPost(ListUrl, Any, ListAnswer(totalCount: 3, "{\"id\":1}"));
        KalturaOttClient client = CreateClient(http);

        await Assert.ThrowsAsync<FormatException>(
            () => client.ListAllAsync(KalturaQueries.SeriesPage, CancellationToken.None));
        Assert.Equal(2, http.Posts.Count(call => call.Url == ListUrl));
    }

    [Fact]
    public async Task WithoutATotalCountOnePageOfAssetsIsTheWholeList()
    {
        FakeReshet13Http http = new();
        http.RespondToPost(LoginUrl, Any, LoginJson);
        http.RespondToPost(ListUrl, IsPage(1), ListAnswerWithoutTotalCount("{\"id\":1}", "{\"id\":2}"));
        http.RespondToPost(ListUrl, IsPage(2), ListAnswerWithoutTotalCount());
        KalturaOttClient client = CreateClient(http);

        IReadOnlyList<JsonElement> assets = await client.ListAllAsync(KalturaQueries.SeriesPage, CancellationToken.None);

        Assert.Equal(2, assets.Count);
    }

    [Fact]
    public async Task WithoutATotalCountAWalkOverSeveralPagesIsAFormatException()
    {
        FakeReshet13Http http = new();
        http.RespondToPost(LoginUrl, Any, LoginJson);
        http.RespondToPost(ListUrl, IsPage(1), ListAnswerWithoutTotalCount("{\"id\":1}"));
        http.RespondToPost(ListUrl, IsPage(2), ListAnswerWithoutTotalCount("{\"id\":2}"));
        http.RespondToPost(ListUrl, IsPage(3), ListAnswerWithoutTotalCount());
        KalturaOttClient client = CreateClient(http);

        await Assert.ThrowsAsync<FormatException>(
            () => client.ListAllAsync(KalturaQueries.SeriesPage, CancellationToken.None));
    }

    /// <summary>The two saved session errors as Kaltura sent them; any other code in the same shape.</summary>
    private static string SessionError(string code) => code switch
    {
        "500004" => Error500004Json,
        "500015" => Error500015Json,
        _ => Error500015Json.Replace("500015", code, StringComparison.Ordinal),
    };

    internal static string ListAnswer(int totalCount, params string[] objects) =>
        $"{{\"executionTime\":0.01,\"result\":{{\"objectType\":\"KalturaAssetListResponse\",\"totalCount\":{totalCount},\"objects\":[{string.Join(',', objects)}]}}}}";

    private static string ListAnswerWithoutTotalCount(params string[] objects) =>
        $"{{\"executionTime\":0.01,\"result\":{{\"objectType\":\"KalturaAssetListResponse\",\"objects\":[{string.Join(',', objects)}]}}}}";

    private static KalturaOttClient CreateClient(FakeReshet13Http http) =>
        new(http, new RecordingLogger<KalturaOttClient>());
}
