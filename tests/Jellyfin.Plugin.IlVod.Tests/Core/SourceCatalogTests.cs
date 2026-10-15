using Jellyfin.Plugin.IlVod.Cache;
using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Tests.Support;

namespace Jellyfin.Plugin.IlVod.Tests.Core;

/// <summary>
/// <see cref="SourceCatalog{TIndex,TShowPage,TSeasonPage,TStream}"/> over a made-up provider whose
/// native records are plain strings — proving the composer needs nothing mako-specific.
/// </summary>
public class SourceCatalogTests
{
    private static readonly TimeSpan StreamLinkLifetime = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task EachAnswerIsFetchedByTheSourceAndConvertedByTheTransformer()
    {
        StringSource source = new();
        SourceCatalog<string, string, string, string> catalog = CatalogOver(source, new FakeClock());

        Show show = Assert.Single(await catalog.GetShowsAsync(CancellationToken.None));
        Season season = Assert.Single(await catalog.GetSeasonsAsync("show1", CancellationToken.None));
        Episode episode = Assert.Single(await catalog.GetEpisodesAsync("show1", "season1", CancellationToken.None));

        Assert.Equal("index", show.Id);
        Assert.Equal("show:show1", season.Id);
        Assert.Equal("season:show1/season1", episode.Id);
    }

    [Fact]
    public async Task AStreamLinkIsReusedWithinItsLifetimeAndResolvedAgainAfter()
    {
        StringSource source = new();
        FakeClock clock = new();
        SourceCatalog<string, string, string, string> catalog = CatalogOver(source, clock);

        StreamLink first = await catalog.GetStreamLinkAsync("ep1", CancellationToken.None);
        StreamLink cached = await catalog.GetStreamLinkAsync("ep1", CancellationToken.None);
        clock.Advance(StreamLinkLifetime + TimeSpan.FromSeconds(1));
        await catalog.GetStreamLinkAsync("ep1", CancellationToken.None);

        Assert.Equal(first, cached);
        Assert.Equal(2, source.StreamFetches);
    }

    private static SourceCatalog<string, string, string, string> CatalogOver(StringSource source, IClock clock) =>
        new(source, new StringTransformer(), new TimedCache(clock), () => StreamLinkLifetime);

    private sealed class StringSource : IContentSource<string, string, string, string>
    {
        public int StreamFetches { get; private set; }

        public Task<string> FetchIndexAsync(CancellationToken cancellationToken) => Task.FromResult("index");

        public Task<string> FetchShowAsync(string showId, CancellationToken cancellationToken) => Task.FromResult($"show:{showId}");

        public Task<string> FetchSeasonAsync(string showId, string seasonId, CancellationToken cancellationToken) =>
            Task.FromResult($"season:{showId}/{seasonId}");

        public Task<string> FetchStreamAsync(string episodeId, CancellationToken cancellationToken)
        {
            StreamFetches++;
            return Task.FromResult($"https://cdn.test/{episodeId}.m3u8");
        }
    }

    private sealed class StringTransformer : IContentTransformer<string, string, string, string>
    {
        public IReadOnlyList<Show> ToShows(string index) => [new Show(index, index, PosterUrl: null, Overview: null, Genres: [])];

        public IReadOnlyList<Season> ToSeasons(string showPage) => [new Season(showPage, 1, showPage)];

        public IReadOnlyList<Episode> ToEpisodes(string seasonPage) =>
            [new Episode(seasonPage, string.Empty, 1, 1, seasonPage, AirDate: null, Duration: null, ThumbnailUrl: null)];

        public StreamLink ToStreamLink(string stream) => new(stream, "hls", new StreamDescription(null, null, null, "Main", null));
    }
}
