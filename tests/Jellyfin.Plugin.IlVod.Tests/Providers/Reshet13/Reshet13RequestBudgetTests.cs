using Jellyfin.Plugin.IlVod.Cache;
using Jellyfin.Plugin.IlVod.Configuration;
using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Providers.Reshet13;
using Jellyfin.Plugin.IlVod.Providers.Reshet13.Source;
using Jellyfin.Plugin.IlVod.Tests.Support;
using static Jellyfin.Plugin.IlVod.Tests.Providers.Reshet13.Reshet13Fixtures;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Reshet13;

/// <summary>
/// N1 for Reshet 13, like <c>OnDemandRequestBudgetTests</c> for mako: one
/// <see cref="FakeReshet13Http"/> under the real <see cref="KalturaOttClient"/> and
/// <see cref="Reshet13Source"/>, joined into an <see cref="ICatalog"/> by
/// <see cref="SourceCatalog{TIndex,TShowPage,TSeasonPage,TStream}"/>, counting requests after every
/// step of a user session. The transformer is a stand-in (<see cref="CountingTransformer"/>): the
/// budget is the source's alone, and the stand-in keeps this test independent of how the real one
/// names things.
/// </summary>
public class Reshet13RequestBudgetTests
{
    private static readonly TimeSpan StreamLinkLifetime = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task TheRequestBudgetMatchesEveryStepOfTheOnDemandTable()
    {
        FakeReshet13Http http = SeededHttp();
        ICatalog catalog = CreateCatalog(http);

        // | Open the channel (cold) | login + 1 (the series list) |
        await catalog.GetShowsAsync(CancellationToken.None);
        Assert.Equal(2, http.Calls.Count);
        Assert.Single(http.Calls, call => call.Url == LoginUrl);

        // | Open the channel again (fresh) | unchanged |
        await catalog.GetShowsAsync(CancellationToken.None);
        Assert.Equal(2, http.Calls.Count);

        // | Open a show | + 2 (its episodes and its season assets) |
        await catalog.GetSeasonsAsync(SeriesId, CancellationToken.None);
        Assert.Equal(4, http.Calls.Count);

        // | Open a season | unchanged: filtered from the cached show page |
        await catalog.GetEpisodesAsync(SeriesId, "4", CancellationToken.None);
        await catalog.GetEpisodesAsync(SeriesId, Reshet13SeasonId.NoSeason, CancellationToken.None);
        Assert.Equal(4, http.Calls.Count);

        // | Press play (entry id known from the show page) | + 1 (the manifest GET): Kaltura states
        // this episode's length, so no variant playlist is read to measure it |
        StreamLink link = await catalog.GetStreamLinkAsync(KnownAssetId, CancellationToken.None);
        Assert.Equal(5, http.Calls.Count);
        Assert.Equal(KnownManifestUrl, http.Calls[^1].Url);
        Assert.Equal(KnownManifestUrl, link.Url);

        // | Same episode again within the stream-link lifetime | unchanged |
        await catalog.GetStreamLinkAsync(KnownAssetId, CancellationToken.None);
        Assert.Equal(5, http.Calls.Count);

        // | Press play on an episode Kaltura states no length for | + 2 (the manifest, then its best variant, to measure it) |
        await catalog.GetStreamLinkAsync(UnstatedLengthAssetId, CancellationToken.None);
        Assert.Equal(7, http.Calls.Count);
        Assert.Equal(BestVariantUrl, http.Calls[^1].Url);

        Assert.Single(http.Calls, call => call.Url == LoginUrl);
    }

    [Fact]
    public async Task OpeningASeasonFirstCostsOnlyItsShowPage()
    {
        FakeReshet13Http http = SeededHttp();
        ICatalog catalog = CreateCatalog(http);

        await catalog.GetEpisodesAsync(SeriesId, "4", CancellationToken.None);

        Assert.Equal(3, http.Calls.Count);
        Assert.DoesNotContain(http.Posts, call => IsSeriesList(call.Body!));
    }

    [Fact]
    public async Task NothingIsRequestedForShowsNobodyOpened()
    {
        FakeReshet13Http http = SeededHttp();
        ICatalog catalog = CreateCatalog(http);

        await catalog.GetShowsAsync(CancellationToken.None);

        Assert.DoesNotContain(http.Posts, call => call.Body!.Contains("SeriesID = ", StringComparison.Ordinal));
        Assert.Contains(http.Posts, call => IsSeriesList(call.Body!));
    }

    private static ICatalog CreateCatalog(FakeReshet13Http http)
    {
        FakeClock clock = new();
        Reshet13Source source = new(
            new KalturaOttClient(http, new RecordingLogger<KalturaOttClient>()),
            http,
            new TimedCache(clock),
            () => Reshet13ServiceCollectionExtensions.LifetimesFrom(new PluginConfiguration()),
            new RecordingLogger<Reshet13Source>());

        return new SourceCatalog<Reshet13ShowIndex, Reshet13ShowPage, Reshet13SeasonPage, Reshet13Stream>(
            source, new CountingTransformer(), new TimedCache(clock), () => StreamLinkLifetime);
    }

    /// <summary>The least a transformer can do: one item per record, so the catalogue has something to pass along.</summary>
    private sealed class CountingTransformer : IContentTransformer<Reshet13ShowIndex, Reshet13ShowPage, Reshet13SeasonPage, Reshet13Stream>
    {
        public IReadOnlyList<Show> ToShows(Reshet13ShowIndex index) =>
            [.. index.Series.Select(series => new Show(series.SeriesId, series.Name, null, series.Overview, series.Genres))];

        public IReadOnlyList<Season> ToSeasons(Reshet13ShowPage showPage) =>
            [.. showPage.Episodes.Select(episode => episode.SeasonIndex).Distinct()
                .Select(index => new Season(Reshet13SeasonId.Of(index), index ?? 0, Reshet13SeasonId.Of(index)))];

        public IReadOnlyList<Episode> ToEpisodes(Reshet13SeasonPage seasonPage) =>
            [.. seasonPage.Episodes.Select(episode => new Episode(episode.AssetId, seasonPage.ShowName, 0, 0, episode.Name, null, episode.Duration, null))];

        public StreamLink ToStreamLink(Reshet13Stream stream) => new(stream.ManifestUrl, "hls", new StreamDescription(null, null, null, "Main", null));
    }
}
