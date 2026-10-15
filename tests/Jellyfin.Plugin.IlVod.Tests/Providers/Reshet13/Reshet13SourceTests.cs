using System.Text.Json.Nodes;
using Jellyfin.Plugin.IlVod.Cache;
using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Providers.Reshet13;
using Jellyfin.Plugin.IlVod.Providers.Reshet13.Source;
using Jellyfin.Plugin.IlVod.Tests.Support;
using static Jellyfin.Plugin.IlVod.Tests.Providers.Reshet13.Reshet13Fixtures;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Reshet13;

public class Reshet13SourceTests
{
    private static readonly Reshet13Lifetimes Lifetimes = Reshet13Lifetimes.Default;
    private static readonly TimeSpan PastEveryLifetime = TimeSpan.FromDays(2);

    private readonly FakeClock _clock = new();

    [Fact]
    public async Task AFreshIndexIsServedWithoutARequest()
    {
        FakeReshet13Http http = SeededHttp();
        Reshet13Source source = CreateSource(http);

        Reshet13ShowIndex first = await source.FetchIndexAsync(CancellationToken.None);
        int requestsAfterFirst = http.Calls.Count;
        Reshet13ShowIndex second = await source.FetchIndexAsync(CancellationToken.None);

        Assert.Equal(2, requestsAfterFirst);
        Assert.Equal(requestsAfterFirst, http.Calls.Count);
        Assert.Equal(first.Series.Select(series => series.SeriesId), second.Series.Select(series => series.SeriesId));
    }

    [Fact]
    public async Task AnExpiredIndexIsFetchedAgain()
    {
        FakeReshet13Http http = SeededHttp();
        Reshet13Source source = CreateSource(http);

        await source.FetchIndexAsync(CancellationToken.None);
        _clock.Advance(Lifetimes.Catalogue + TimeSpan.FromMinutes(1));
        await source.FetchIndexAsync(CancellationToken.None);

        Assert.Equal(2, http.Posts.Count(call => call.Url == ListUrl));
    }

    [Fact]
    public async Task TheIndexIsReadFromEveryPageUntilTheTotalCount()
    {
        FakeReshet13Http http = new();
        http.RespondToPost(LoginUrl, Any, LoginJson);
        http.RespondToPost(ListUrl, IsPage(1), KalturaOttClientTests.ListAnswer(2, SeriesAsset("1")));
        http.RespondToPost(ListUrl, IsPage(2), KalturaOttClientTests.ListAnswer(2, SeriesAsset("2")));
        Reshet13Source source = CreateSource(http);

        Reshet13ShowIndex index = await source.FetchIndexAsync(CancellationToken.None);

        Assert.Equal(["1", "2"], index.Series.Select(series => series.SeriesId));
        Assert.Equal(2, http.Posts.Count(call => call.Url == ListUrl));
    }

    [Fact]
    public async Task AnEmptyCatalogueIsRefusedAndNeverCached()
    {
        FakeReshet13Http http = new();
        http.RespondToPost(LoginUrl, Any, LoginJson);
        http.RespondToPost(ListUrl, IsSeriesList, KalturaOttClientTests.ListAnswer(0));
        Reshet13Source source = CreateSource(http);

        await Assert.ThrowsAsync<FormatException>(() => source.FetchIndexAsync(CancellationToken.None));
        await Assert.ThrowsAsync<FormatException>(() => source.FetchIndexAsync(CancellationToken.None));

        Assert.Equal(2, http.Posts.Count(call => call.Url == ListUrl));
    }

    [Fact]
    public async Task AFailureWithNothingCachedIsThrownAndNeverCached()
    {
        FakeReshet13Http http = SeededHttp();
        http.FailPost(ListUrl, IsSeriesList);
        Reshet13Source source = CreateSource(http);

        await Assert.ThrowsAsync<Reshet13UnavailableException>(() => source.FetchIndexAsync(CancellationToken.None));
        http.ForgetPost(ListUrl, "\"typeIn\":\"1259\"");
        http.RespondToPost(ListUrl, IsSeriesList, SeriesJson);
        Reshet13ShowIndex index = await source.FetchIndexAsync(CancellationToken.None);

        Assert.Equal(6, index.Series.Count);
    }

    [Fact]
    public async Task AStaleIndexIsServedWhenKalturaFails()
    {
        FakeReshet13Http http = SeededHttp();
        Reshet13Source source = CreateSource(http);
        await source.FetchIndexAsync(CancellationToken.None);

        _clock.Advance(PastEveryLifetime);
        http.FailPost(ListUrl, IsSeriesList);
        Reshet13ShowIndex stale = await source.FetchIndexAsync(CancellationToken.None);

        Assert.Equal(6, stale.Series.Count);
    }

    [Fact]
    public async Task OpeningAShowAsksForItsEpisodesAndItsSeasonAssetsOnce()
    {
        FakeReshet13Http http = SeededHttp();
        Reshet13Source source = CreateSource(http);

        Reshet13ShowPage page = await source.FetchShowAsync(SeriesId, CancellationToken.None);
        await source.FetchShowAsync(SeriesId, CancellationToken.None);

        Assert.Equal(9, page.Episodes.Count);
        Assert.Equal("עונה 5", page.SeasonAssetNames[4]);
        Assert.Single(http.Posts, call => call.Url == ListUrl && IsEpisodesOf57(call.Body!));
        Assert.Single(http.Posts, call => call.Url == ListUrl && IsSeasonAssetsOf57(call.Body!));
    }

    [Fact]
    public async Task TheParsedShowPageOutlivesTheSourceThatFetchedIt()
    {
        TimedCache cache = new(_clock);
        await CreateSource(SeededHttp(), cache).FetchShowAsync(SeriesId, CancellationToken.None);
        FakeReshet13Http http = SeededHttp();

        Reshet13ShowPage page = await CreateSource(http, cache).FetchShowAsync(SeriesId, CancellationToken.None);

        Assert.Equal(9, page.Episodes.Count);
        Assert.Empty(http.Calls);
    }

    [Fact]
    public async Task AStaleShowPageIsServedWhenKalturaFails()
    {
        FakeReshet13Http http = SeededHttp();
        Reshet13Source source = CreateSource(http);
        await source.FetchShowAsync(SeriesId, CancellationToken.None);

        _clock.Advance(PastEveryLifetime);
        http.FailPost(ListUrl, IsEpisodesOf57);
        Reshet13ShowPage stale = await source.FetchShowAsync(SeriesId, CancellationToken.None);

        Assert.Equal(9, stale.Episodes.Count);
    }

    [Theory]
    [InlineData("57' or asset_type = '1259")]
    [InlineData("")]
    [InlineData("5 7")]
    public async Task ASeriesIdThatIsNotDigitsIsRefusedBeforeAnyRequest(string seriesId)
    {
        FakeReshet13Http http = SeededHttp();
        Reshet13Source source = CreateSource(http);

        await Assert.ThrowsAsync<ArgumentException>(() => source.FetchShowAsync(seriesId, CancellationToken.None));

        Assert.Empty(http.Calls);
    }

    [Fact]
    public async Task OpeningASeasonOfAFreshShowMakesNoRequest()
    {
        FakeReshet13Http http = SeededHttp();
        Reshet13Source source = CreateSource(http);
        await source.FetchShowAsync(SeriesId, CancellationToken.None);
        int requestsBefore = http.Calls.Count;

        await source.FetchSeasonAsync(SeriesId, "4", CancellationToken.None);

        Assert.Equal(requestsBefore, http.Calls.Count);
    }

    [Theory]
    [InlineData("4", new[] { "2361602", "2353654" })]
    [InlineData("3", new[] { "3043844", "2164350" })]
    [InlineData(Reshet13SeasonId.NoSeason, new[] { "2207940" })]
    public async Task ASeasonIsTheShowsEpisodesWithItsIndex(string seasonId, string[] expectedAssetIds)
    {
        Reshet13Source source = CreateSource(SeededHttp());

        Reshet13SeasonPage season = await source.FetchSeasonAsync(SeriesId, seasonId, CancellationToken.None);

        Assert.Equal(expectedAssetIds, season.Episodes.Select(episode => episode.AssetId));
        Assert.Equal(Reshet13SeasonId.Parse(seasonId), season.SeasonIndex);
    }

    [Fact]
    public async Task ASeasonCarriesItsSeasonAssetNameWhenTheShowHasOne()
    {
        Reshet13Source source = CreateSource(SeededHttp());

        Reshet13SeasonPage named = await source.FetchSeasonAsync(SeriesId, "4", CancellationToken.None);
        Reshet13SeasonPage unnamed = await source.FetchSeasonAsync(SeriesId, "1", CancellationToken.None);

        Assert.Equal("עונה 5", named.SeasonAssetName);
        Assert.Null(unnamed.SeasonAssetName);
    }

    [Fact]
    public async Task ASeasonIsNamedAfterItsShowFromTheEpisodesSeriesName()
    {
        Reshet13Source source = CreateSource(SeededHttp());

        Reshet13SeasonPage season = await source.FetchSeasonAsync(SeriesId, "4", CancellationToken.None);

        Assert.Equal("האח הגדול", season.ShowName);
    }

    [Fact]
    public async Task WithoutASeriesNameTheShowNameComesFromTheCachedIndex()
    {
        FakeReshet13Http http = SeededHttp();
        http.ForgetPost(ListUrl, "SeriesID = '57' asset_type = '1268'");
        http.RespondToPost(ListUrl, IsEpisodesOf57, EpisodesWithoutSeriesNames());
        Reshet13Source source = CreateSource(http);
        await source.FetchIndexAsync(CancellationToken.None);

        Reshet13SeasonPage season = await source.FetchSeasonAsync(SeriesId, "4", CancellationToken.None);

        Assert.Equal("האח הגדול", season.ShowName);
    }

    [Fact]
    public async Task WithoutASeriesNameOrACachedIndexTheShowNameIsEmptyAndTheIndexIsNeverFetched()
    {
        FakeReshet13Http http = SeededHttp();
        http.ForgetPost(ListUrl, "SeriesID = '57' asset_type = '1268'");
        http.RespondToPost(ListUrl, IsEpisodesOf57, EpisodesWithoutSeriesNames());
        Reshet13Source source = CreateSource(http);

        Reshet13SeasonPage season = await source.FetchSeasonAsync(SeriesId, "4", CancellationToken.None);

        Assert.Equal(string.Empty, season.ShowName);
        Assert.DoesNotContain(http.Posts, call => IsSeriesList(call.Body!));
    }

    [Fact]
    public async Task PlayingAnEpisodeOfAnOpenedShowOnlyFetchesTheManifest()
    {
        FakeReshet13Http http = SeededHttp();
        Reshet13Source source = CreateSource(http);
        await source.FetchShowAsync(SeriesId, CancellationToken.None);
        int postsBefore = http.Posts.Count;

        Reshet13Stream stream = await source.FetchStreamAsync(KnownAssetId, CancellationToken.None);

        Assert.Equal(postsBefore, http.Posts.Count);
        Assert.Equal([KnownManifestUrl], http.Gets.Select(call => call.Url));
        Assert.Equal(KnownManifestUrl, stream.ManifestUrl);
        Assert.Equal(TimeSpan.FromSeconds(5552), stream.Duration);
        Assert.Equal(1280, stream.Variant?.Width);
    }

    [Fact]
    public async Task PlayingAnEpisodeOfAShowReadFromTheCacheStillNeedsNoAssetRequest()
    {
        TimedCache cache = new(_clock);
        await CreateSource(SeededHttp(), cache).FetchShowAsync(SeriesId, CancellationToken.None);
        FakeReshet13Http http = SeededHttp();
        Reshet13Source source = CreateSource(http, cache);
        await source.FetchShowAsync(SeriesId, CancellationToken.None);

        await source.FetchStreamAsync(KnownAssetId, CancellationToken.None);

        Assert.Empty(http.Posts);
    }

    [Fact]
    public async Task PlayingAnUnknownEpisodeAsksForItsAssetThenTheManifest()
    {
        FakeReshet13Http http = SeededHttp();
        Reshet13Source source = CreateSource(http);

        Reshet13Stream stream = await source.FetchStreamAsync(AssetGetId, CancellationToken.None);

        Assert.Single(http.Posts, call => call.Url == GetUrl);
        Assert.Equal([AssetGetManifestUrl], http.Gets.Select(call => call.Url));
        Assert.Equal(TimeSpan.FromSeconds(3822), stream.Duration);
    }

    [Fact]
    public async Task AnEpisodeWithNoHlsFileIsNotPlayable()
    {
        FakeReshet13Http http = new();
        http.RespondToPost(LoginUrl, Any, LoginJson);
        http.RespondToPost(GetUrl, IsAssetGet, $$$"""{"result":{"id":{{{AssetGetId}}},"name":"x","mediaFiles":[]}}""");
        Reshet13Source source = CreateSource(http);

        await Assert.ThrowsAsync<StreamUnavailableException>(() => source.FetchStreamAsync(AssetGetId, CancellationToken.None));

        Assert.Empty(http.Gets);
    }

    [Fact]
    public async Task AnAssetAnswerThatIsNoEpisodeIsNotPlayable()
    {
        FakeReshet13Http http = new();
        http.RespondToPost(LoginUrl, Any, LoginJson);
        http.RespondToPost(GetUrl, IsAssetGet, """{"result":{"name":"no id"}}""");
        Reshet13Source source = CreateSource(http);

        await Assert.ThrowsAsync<StreamUnavailableException>(() => source.FetchStreamAsync(AssetGetId, CancellationToken.None));
    }

    [Theory]
    [InlineData("4471744/../x")]
    [InlineData("abc")]
    [InlineData("")]
    public async Task AnAssetIdThatIsNotDigitsIsRefusedBeforeAnyRequest(string assetId)
    {
        FakeReshet13Http http = SeededHttp();
        Reshet13Source source = CreateSource(http);

        await Assert.ThrowsAsync<StreamUnavailableException>(() => source.FetchStreamAsync(assetId, CancellationToken.None));

        Assert.Empty(http.Calls);
    }

    [Fact]
    public async Task AManifestThatCannotBeReadStillPlaysUndescribed()
    {
        FakeReshet13Http http = SeededHttp();
        http.FailGet(AssetGetManifestUrl);
        RecordingLogger<Reshet13Source> logger = new();
        Reshet13Source source = CreateSource(http, new TimedCache(_clock), logger);

        Reshet13Stream stream = await source.FetchStreamAsync(AssetGetId, CancellationToken.None);

        Assert.Equal(AssetGetManifestUrl, stream.ManifestUrl);
        Assert.Null(stream.Variant);
        Assert.Equal(TimeSpan.FromSeconds(3822), stream.Duration);
        Assert.Single(logger.Warnings);
    }

    [Fact]
    public async Task AnEpisodeThatStatesNoLengthIsMeasuredFromItsBestVariant()
    {
        FakeReshet13Http http = SeededHttp();
        Reshet13Source source = CreateSource(http);
        await source.FetchShowAsync(SeriesId, CancellationToken.None);

        Reshet13Stream stream = await source.FetchStreamAsync(UnstatedLengthAssetId, CancellationToken.None);

        Assert.Equal([UnstatedLengthManifestUrl, BestVariantUrl], http.Gets.Select(call => call.Url));
        Assert.Equal(VariantPlaylistLength, stream.Duration);
        Assert.Equal(1280, stream.Variant?.Width);
    }

    [Fact]
    public async Task AnEpisodeThatStatesItsLengthIsNotMeasured()
    {
        FakeReshet13Http http = SeededHttp();
        Reshet13Source source = CreateSource(http);
        await source.FetchShowAsync(SeriesId, CancellationToken.None);

        await source.FetchStreamAsync(KnownAssetId, CancellationToken.None);

        Assert.DoesNotContain(http.Gets, call => call.Url == BestVariantUrl);
    }

    [Fact]
    public async Task AVariantThatCannotBeReadLeavesTheLengthUnstatedAndNeverNamesItsSignature()
    {
        FakeReshet13Http http = SeededHttp();
        http.FailGet(BestVariantUrl);
        RecordingLogger<Reshet13Source> logger = new();
        Reshet13Source source = CreateSource(http, new TimedCache(_clock), logger);
        await source.FetchShowAsync(SeriesId, CancellationToken.None);

        Reshet13Stream stream = await source.FetchStreamAsync(UnstatedLengthAssetId, CancellationToken.None);

        Assert.Null(stream.Duration);
        Assert.Equal(1280, stream.Variant?.Width);
        string warning = Assert.Single(logger.Warnings);
        Assert.DoesNotContain("Signature", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoLogLineEverNamesTheSession()
    {
        FakeReshet13Http http = SeededHttp();
        http.FailGet(AssetGetManifestUrl);
        RecordingLogger<Reshet13Source> logger = new();
        Reshet13Source source = CreateSource(http, new TimedCache(_clock), logger);
        await source.FetchIndexAsync(CancellationToken.None);
        _clock.Advance(PastEveryLifetime);
        http.FailPost(ListUrl, IsSeriesList);

        await source.FetchIndexAsync(CancellationToken.None);
        await source.FetchStreamAsync(AssetGetId, CancellationToken.None);

        Assert.NotEmpty(logger.Lines);
        Assert.All(logger.Lines, line => Assert.DoesNotContain(Ks, line, StringComparison.Ordinal));
    }

    private Reshet13Source CreateSource(FakeReshet13Http http) => CreateSource(http, new TimedCache(_clock));

    private static Reshet13Source CreateSource(FakeReshet13Http http, TimedCache cache) =>
        CreateSource(http, cache, new RecordingLogger<Reshet13Source>());

    private static Reshet13Source CreateSource(FakeReshet13Http http, TimedCache cache, RecordingLogger<Reshet13Source> logger) =>
        new(new KalturaOttClient(http, new RecordingLogger<KalturaOttClient>()), http, cache, () => Lifetimes, logger);

    private static string SeriesAsset(string seriesId) =>
        "{\"name\":\"series " + seriesId + "\",\"metas\":{\"SeriesID\":{\"value\":\"" + seriesId + "\"}}}";

    private static string EpisodesWithoutSeriesNames()
    {
        JsonNode answer = JsonNode.Parse(EpisodesJson)!;
        foreach (JsonNode? episode in answer["result"]!["objects"]!.AsArray())
        {
            episode!["tags"]!.AsObject().Remove("SeriesName");
        }

        return answer.ToJsonString();
    }
}
