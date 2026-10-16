using Jellyfin.Plugin.IlVod.Cache;
using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Providers.Kan;
using Jellyfin.Plugin.IlVod.Providers.Kan.Source;
using Jellyfin.Plugin.IlVod.Providers.Kan.Transform;
using Jellyfin.Plugin.IlVod.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Kan;

/// <summary>
/// N1 for Kan, like <c>Reshet13RequestBudgetTests</c>: one <see cref="FakeKanHttp"/> under the real
/// <see cref="KanSource"/> and <see cref="KanTransformer"/>, joined by
/// <see cref="SourceCatalog{TIndex,TShowPage,TSeasonPage,TStream}"/>, counting requests after every
/// step of a user session. The catalogue is two genres of one page each.
/// </summary>
public class KanRequestBudgetTests
{
    private const string Satire = "11510";
    private const string DramaSlug = "%D7%93%D7%A8%D7%9E%D7%94";
    private const string ComedySlug = "%D7%A7%D7%95%D7%9E%D7%93%D7%99%D7%94";
    private const string BestVariantUrl =
        "https://r.il.cdn-redge.media/webcache/gorigin/hls/oil/kancdn/vod/2253468/LIBCODER_SMOOTH_1080_KAN/Manifest.ism/playlist.m3u8?id=1&bitrate=5500000&fmp4";

    private static readonly string EpisodeId = KanEpisodeId.Of(Satire, "1107310");
    private static readonly TimeSpan StreamLinkLifetime = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task TheRequestBudgetMatchesEveryStepOfTheOnDemandTable()
    {
        FakeKanHttp http = SeededHttp();
        ICatalog catalog = CreateCatalog(http);

        // | Open the channel (cold) | the lobby + every genre page |
        await catalog.GetShowsAsync(CancellationToken.None);
        Assert.Equal(1 + 2, http.Requests.Count);

        // | Open the channel again (fresh) | unchanged |
        await catalog.GetShowsAsync(CancellationToken.None);
        Assert.Equal(3, http.Requests.Count);

        // | Open a show | + 1 (its feed) |
        await catalog.GetSeasonsAsync(Satire, CancellationToken.None);
        Assert.Equal(4, http.Requests.Count);

        // | Open a season | unchanged: filtered from the cached feed |
        await catalog.GetEpisodesAsync(Satire, "6", CancellationToken.None);
        await catalog.GetEpisodesAsync(Satire, KanSeasonId.NoSeason, CancellationToken.None);
        Assert.Equal(4, http.Requests.Count);

        // | Press play | + 2 (the episode page, its master playlist): the feed stated the length |
        StreamLink link = await catalog.GetStreamLinkAsync(EpisodeId, CancellationToken.None);
        Assert.Equal(6, http.Requests.Count);
        Assert.Equal(new TimeSpan(0, 29, 35), link.Description.Duration);
        Assert.Equal((5, 0), (link.Description.VideoStreamIndex, link.Description.AudioStreamIndex));

        // | Same episode again within the stream-link lifetime | unchanged |
        await catalog.GetStreamLinkAsync(EpisodeId, CancellationToken.None);
        Assert.Equal(6, http.Requests.Count);
    }

    /// <summary>An episode of a show nobody opened since the restart: play still needs neither the
    /// show list nor the feed, only one more request to measure the length off the best variant.</summary>
    [Fact]
    public async Task PlayingAnEpisodeOfAnUnopenedShowFetchesNoListAndMeasuresItsLength()
    {
        FakeKanHttp http = SeededHttp();
        ICatalog catalog = CreateCatalog(http);

        StreamLink link = await catalog.GetStreamLinkAsync(EpisodeId, CancellationToken.None);

        Assert.Equal(3, http.Requests.Count);
        Assert.Equal(BestVariantUrl, http.Requests[^1]);
        Assert.NotNull(link.Description.Duration);
    }

    [Fact]
    public async Task OpeningASeasonFirstCostsOnlyItsShowsFeed()
    {
        FakeKanHttp http = SeededHttp();
        ICatalog catalog = CreateCatalog(http);

        await catalog.GetEpisodesAsync(Satire, "6", CancellationToken.None);

        Assert.Equal([KanUrls.ProgramFeed(Satire)], http.Requests);
    }

    private static ICatalog CreateCatalog(FakeKanHttp http)
    {
        FakeClock clock = new();
        KanSource source = new(
            http,
            new TimedCache(clock),
            () => new KanLifetimes(Catalogue: TimeSpan.FromHours(24), Show: TimeSpan.FromHours(6)),
            NullLogger<KanSource>.Instance);
        return new SourceCatalog<KanShowIndex, KanShowPage, KanSeasonPage, KanStream>(
            source, new KanTransformer(), new TimedCache(clock), () => StreamLinkLifetime);
    }

    private static FakeKanHttp SeededHttp()
    {
        FakeKanHttp http = new();
        http.RespondTo(KanUrls.Lobby, KanHtml.Lobby(("דרמה", DramaSlug), ("קומדיה", ComedySlug)));
        http.RespondTo(KanUrls.GenrePage(DramaSlug, 1), KanHtml.GenrePage(2, null, KanHtml.Card(Satire), KanHtml.Card("2")));
        http.RespondTo(KanUrls.GenrePage(ComedySlug, 1), KanHtml.GenrePage(1, null, KanHtml.Card(Satire)));
        http.RespondTo(KanUrls.ProgramFeed(Satire), FixtureFile.ReadText("kan-program-11510.json"));
        http.RespondTo(KanUrls.EpisodePage(Satire, "1107310"), FixtureFile.ReadText("kan-episode-page.html"));
        http.RespondTo(KanPlayerConfigTests.MasterUrl, FixtureFile.ReadText("kan-master.m3u8"));
        http.RespondTo(BestVariantUrl, "#EXTM3U\n#EXTINF:600.0,\na.mp4\n#EXTINF:1175.0,\nb.mp4\n#EXT-X-ENDLIST\n");
        return http;
    }
}
