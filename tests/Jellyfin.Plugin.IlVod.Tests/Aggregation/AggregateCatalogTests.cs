using Jellyfin.Plugin.IlVod.Aggregation;
using Jellyfin.Plugin.IlVod.Cache;
using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Providers.Mako;
using Jellyfin.Plugin.IlVod.Tests.Support;

namespace Jellyfin.Plugin.IlVod.Tests.Aggregation;

public class AggregateCatalogTests
{
    [Fact]
    public async Task TheRootListsEveryProvidersShowsWithIdsScopedToTheirProvider()
    {
        AggregateCatalog catalog = CatalogOver(new FakeProvider("alpha", new FakeCatalog()), new FakeProvider("beta", new FakeCatalog()));

        IReadOnlyList<Show> shows = await catalog.GetShowsAsync(CancellationToken.None);

        Assert.Equal(
            ["alpha:shemesh", "alpha:eretz_nehederet", "beta:shemesh", "beta:eretz_nehederet"],
            shows.Select(show => show.Id));
    }

    [Fact]
    public async Task AFailingProviderWithNoSnapshotIsLeftOutWhileTheOthersStillList()
    {
        FakeCatalog failing = new() { BrowseFailure = new InvalidOperationException("down") };
        AggregateCatalog catalog = CatalogOver(new FakeProvider("alpha", failing), new FakeProvider("beta", new FakeCatalog()));

        IReadOnlyList<Show> shows = await catalog.GetShowsAsync(CancellationToken.None);

        Assert.All(shows, show => Assert.StartsWith("beta:", show.Id, StringComparison.Ordinal));
        Assert.Equal(2, shows.Count);
    }

    [Fact]
    public async Task AFailingProviderAnswersWithItsLastGoodShowList()
    {
        FakeCatalog alpha = new();
        ShowListSnapshots snapshots = new(new TimedCache(new FakeClock()));
        AggregateCatalog catalog = CatalogOver(snapshots, new FakeProvider("alpha", alpha), new FakeProvider("beta", new FakeCatalog()));
        IReadOnlyList<Show> whileUp = await catalog.GetShowsAsync(CancellationToken.None);

        alpha.BrowseFailure = new InvalidOperationException("down");
        IReadOnlyList<Show> whileDown = await catalog.GetShowsAsync(CancellationToken.None);

        Assert.Equal(whileUp.Select(Comparable), whileDown.Select(Comparable));
    }

    [Fact]
    public async Task TheLastGoodShowListSurvivesARestart()
    {
        string path = Path.Combine(Path.GetTempPath(), $"il-vod-snapshots-{Guid.NewGuid():N}.json");
        try
        {
            FakeClock clock = new();
            await CatalogOver(new ShowListSnapshots(new TimedCache(clock, path)), new FakeProvider("alpha", new FakeCatalog()))
                .GetShowsAsync(CancellationToken.None);

            FakeCatalog failing = new() { BrowseFailure = new InvalidOperationException("down") };
            IReadOnlyList<Show> afterRestart = await CatalogOver(new ShowListSnapshots(new TimedCache(clock, path)), new FakeProvider("alpha", failing))
                .GetShowsAsync(CancellationToken.None);

            Assert.Equal(["alpha:shemesh", "alpha:eretz_nehederet"], afterRestart.Select(show => show.Id));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task WhenEveryProviderFailsWithNoSnapshotTheRootFailsRatherThanAnsweringEmpty()
    {
        InvalidOperationException down = new("down");
        AggregateCatalog catalog = CatalogOver(new FakeProvider("alpha", new FakeCatalog { BrowseFailure = down }));

        NoProviderAnsweredException exception = await Assert.ThrowsAsync<NoProviderAnsweredException>(
            () => catalog.GetShowsAsync(CancellationToken.None));

        Assert.Same(down, exception.InnerException);
    }

    [Fact]
    public async Task WithNoProvidersTheRootIsEmpty()
    {
        Assert.Empty(await CatalogOver().GetShowsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SeasonsEpisodesAndPlayAreRoutedToTheProviderTheShowCameFrom()
    {
        FakeCatalog alpha = new();
        FakeCatalog beta = new() { StreamLink = new StreamLink("https://cdn.test/beta.m3u8", "hls", MakoStreams.DefaultDescription) };
        AggregateCatalog catalog = CatalogOver(new FakeProvider("alpha", alpha), new FakeProvider("beta", beta));

        IReadOnlyList<Season> seasons = await catalog.GetSeasonsAsync("beta:shemesh", CancellationToken.None);
        IReadOnlyList<Episode> episodes = await catalog.GetEpisodesAsync("beta:shemesh", seasons[0].Id, CancellationToken.None);
        StreamLink link = await catalog.GetStreamLinkAsync(episodes[0].Id, CancellationToken.None);

        Assert.Equal(("shemesh", "shemesh-s1"), beta.LastEpisodesRequest);
        Assert.Equal(0, alpha.BrowseCalls);
        Assert.Equal("beta:shemesh-s1-e1", episodes[0].Id);
        Assert.Equal("https://cdn.test/beta.m3u8", link.Url);
    }

    [Theory]
    [InlineData("gamma:shemesh")]
    [InlineData("shemesh")]
    public async Task AnIdNamingNoKnownProviderOpensEmptyAndCannotPlay(string id)
    {
        AggregateCatalog catalog = CatalogOver(new FakeProvider("alpha", new FakeCatalog()));

        Assert.Empty(await catalog.GetSeasonsAsync(id, CancellationToken.None));
        Assert.Empty(await catalog.GetEpisodesAsync(id, "shemesh-s1", CancellationToken.None));
        await Assert.ThrowsAsync<StreamUnavailableException>(() => catalog.GetStreamLinkAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task AProviderFailureInsideAShowFailsThatFolderOnly()
    {
        FakeCatalog failing = new() { BrowseFailure = new InvalidOperationException("down") };
        AggregateCatalog catalog = CatalogOver(new FakeProvider("alpha", failing));

        await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.GetSeasonsAsync("alpha:shemesh", CancellationToken.None));
    }

    [Fact]
    public void TwoProvidersWithTheSameIdAreAProgrammingError()
    {
        Assert.Throws<ArgumentException>(() => CatalogOver(new FakeProvider("alpha", new FakeCatalog()), new FakeProvider("alpha", new FakeCatalog())));
    }

    /// <summary>A show as a value: <see cref="Show.Genres"/> is a list, which record equality
    /// compares by reference.</summary>
    private static (string Id, string Name, string? PosterUrl, string Genres) Comparable(Show show) =>
        (show.Id, show.Name, show.PosterUrl, string.Join("|", show.Genres));

    private static AggregateCatalog CatalogOver(params IContentProvider[] providers) =>
        CatalogOver(new ShowListSnapshots(new TimedCache(new FakeClock())), providers);

    private static AggregateCatalog CatalogOver(ShowListSnapshots snapshots, params IContentProvider[] providers) =>
        new(providers, snapshots, new RecordingLogger<AggregateCatalog>());
}
