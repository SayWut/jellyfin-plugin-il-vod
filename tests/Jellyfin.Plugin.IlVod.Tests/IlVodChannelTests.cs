using Jellyfin.Plugin.IlVod.Browsing;
using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Providers.Mako;
using Jellyfin.Plugin.IlVod.Providers.Mako.Source;
using Jellyfin.Plugin.IlVod.Tests.Support;
using Jellyfin.Plugin.IlVod.Tests.Transcoding;
using Jellyfin.Plugin.IlVod.Transcoding;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IlVod.Tests;

[Collection(ProbeSizeLimiterStatusCollection.Name)]
public class IlVodChannelTests
{
    private const string ResolvedUrl = "https://example.test/master.m3u8";
    private const string ResolvedContainer = "hls";

    private static readonly StreamLink ResolvedLink = new(ResolvedUrl, ResolvedContainer, MakoStreams.DefaultDescription);

    private static IlVodChannel ChannelOver(FakeCatalog catalog) => ChannelOver(catalog, NullLogger<IlVodChannel>.Instance);

    /// <summary>The catalogue reaches the channel as a <see cref="Lazy{T}"/> so constructing the
    /// channel touches nothing — see <c>PluginServiceRegistratorTests</c> for why.</summary>
    private static IlVodChannel ChannelOver(FakeCatalog catalog, ILogger<IlVodChannel> logger) =>
        new(new Lazy<ICatalog>(() => catalog), logger);

    private static readonly Guid SomeUser = Guid.Parse("11111111-2222-3333-4444-555555555555");

    /// <summary>A folder opened by a person: Jellyfin passes on the id of the user who clicked.</summary>
    private static Task<ChannelItemResult> Open(IlVodChannel channel, string? folderId)
        => channel.GetChannelItems(new InternalChannelItemQuery { FolderId = folderId, UserId = SomeUser }, CancellationToken.None);

    /// <summary>A folder opened by Jellyfin itself (a metadata refresh walking a new season):
    /// the query carries no user.</summary>
    private static Task<ChannelItemResult> OpenAsJellyfinItself(IlVodChannel channel, string? folderId)
        => channel.GetChannelItems(new InternalChannelItemQuery { FolderId = folderId, UserId = Guid.Empty }, CancellationToken.None);

    [Fact]
    public async Task OpeningTheChannelListsTheShowsAsFolders()
    {
        ChannelItemResult result = await Open(ChannelOver(new FakeCatalog()), folderId: null);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.TotalRecordCount);
        Assert.All(result.Items, item => Assert.Equal(ChannelItemType.Folder, item.Type));
        Assert.Equal("series|shemesh", result.Items[0].Id);
    }

    [Fact]
    public async Task OpeningAShowListsItsSeasons()
    {
        ChannelItemResult result = await Open(ChannelOver(new FakeCatalog()), "series|shemesh");

        Assert.Equal(new[] { "season|shemesh|shemesh-s1", "season|shemesh|shemesh-s2" }, result.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task OpeningASeasonListsItsPlayableEpisodes()
    {
        ChannelItemResult result = await Open(ChannelOver(new FakeCatalog()), "season|shemesh|shemesh-s1");

        Assert.Equal(3, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal(ChannelItemType.Media, item.Type));
    }

    [Fact]
    public async Task OpeningASeasonPassesBothItsShowAndItselfToTheCatalogue()
    {
        var catalog = new FakeCatalog();

        await Open(ChannelOver(catalog), "season|shemesh|shemesh-s1");

        Assert.Equal(("shemesh", "shemesh-s1"), catalog.LastEpisodesRequest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("series|shemesh")]
    [InlineData("season|shemesh|shemesh-s1")]
    public async Task AFolderOpenedByJellyfinItselfIsRefusedWithoutTouchingTheCatalogue(string? folderId)
    {
        var catalog = new FakeCatalog();

        await Assert.ThrowsAsync<NotOpenedByAUserException>(() => OpenAsJellyfinItself(ChannelOver(catalog), folderId));

        Assert.Equal(0, catalog.BrowseCalls);
    }

    [Fact]
    public async Task PressingPlayResolvesTheStreamAtThatMoment()
    {
        FakeCatalog catalog = new() { StreamLink = ResolvedLink };

        IEnumerable<MediaSourceInfo> sources = await ChannelOver(catalog).GetChannelItemMediaInfo("mako:shemesh-s1-e1", CancellationToken.None);

        MediaSourceInfo source = Assert.Single(sources);
        Assert.Equal(ResolvedUrl, source.Path);
        Assert.Equal("mako", MediaSourceFactory.ProviderIdOf(source));
    }

    [Fact]
    public void DoesNotAskJellyfinToCrawlItForLatestItems()
    {
        Assert.False(typeof(ISupportsLatestMedia).IsAssignableFrom(typeof(IlVodChannel)));
    }

    [Fact]
    public void DoesNotAskJellyfinToProbeTheStreamAtBrowseTime()
    {
        Assert.False(typeof(ISupportsMediaProbe).IsAssignableFrom(typeof(IlVodChannel)));
    }

    [Fact]
    public async Task PressingPlayWithNoResolvableStreamPropagatesStreamUnavailable()
    {
        FakeCatalog catalog = new() { StreamLinkFailure = new StreamUnavailableException("no stream available") };

        await Assert.ThrowsAsync<StreamUnavailableException>(
            () => ChannelOver(catalog).GetChannelItemMediaInfo("mako:shemesh-s1-e1", CancellationToken.None));
    }

    [Fact]
    public async Task OpeningAFolderWithAGarbageIdReturnsAnEmptyResultInsteadOfThrowing()
    {
        ChannelItemResult result = await Open(ChannelOver(new FakeCatalog()), "garbage");

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalRecordCount);
    }

    /// <summary>
    /// Jellyfin 12.1 takes any answer as the folder's full contents: it caches it for 3 hours and
    /// deletes every item it holds that the answer lacks. So a failed browse must never be
    /// answered with an empty list — it leaves as the failure it is, which Jellyfin neither
    /// caches nor acts on, and the next open asks again.
    /// </summary>
    [Fact]
    public async Task ABotChallengeWhileBrowsingFailsTheFolderInsteadOfAnsweringEmpty()
    {
        FakeCatalog catalog = new() { BrowseFailure = new BotChallengeException("https://www.mako.co.il/mako-vod/shemesh?query=secret") };
        var logger = new RecordingLogger<IlVodChannel>();

        await Assert.ThrowsAsync<BotChallengeException>(() => Open(ChannelOver(catalog, logger), folderId: null));

        Assert.Contains(logger.Warnings, message => message.Contains("bot challenge", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logger.Lines, line => line.Contains("query=secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AMakoRequestFailureWhileBrowsingFailsTheFolderInsteadOfAnsweringEmpty()
    {
        FakeCatalog catalog = new() { BrowseFailure = new MakoRequestException("HTTP 500 for www.mako.co.il/mako-vod/shemesh") };
        var logger = new RecordingLogger<IlVodChannel>();

        await Assert.ThrowsAsync<MakoRequestException>(() => Open(ChannelOver(catalog, logger), "series|shemesh"));

        Assert.NotEmpty(logger.Warnings);
    }

    /// <summary>mako answering an error page (HTML, status 200) makes the catalogue throw
    /// <see cref="FormatException"/>: a browse failure like any other.</summary>
    [Fact]
    public async Task APageThatIsNotValidJsonFailsTheFolderInsteadOfAnsweringEmpty()
    {
        FakeCatalog catalog = new() { BrowseFailure = new FormatException("mako page is not valid JSON") };
        var logger = new RecordingLogger<IlVodChannel>();

        await Assert.ThrowsAsync<FormatException>(() => Open(ChannelOver(catalog, logger), "series|shemesh"));

        Assert.NotEmpty(logger.Warnings);
    }

    /// <summary>
    /// A folder id is client input, so the id inside it can be a page path mako's URL builder
    /// refuses (<see cref="ArgumentException"/>) — an empty view, never a server error.
    /// </summary>
    [Fact]
    public async Task ASeasonIdThatIsNotAMakoPagePathShowsAnEmptyFolderInsteadOfFailing()
    {
        FakeCatalog catalog = new() { BrowseFailure = new ArgumentException("not a mako page path", "pagePath") };
        var logger = new RecordingLogger<IlVodChannel>();

        ChannelItemResult result = await Open(ChannelOver(catalog, logger), "season|shemesh|@evil.example/x");

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalRecordCount);
        Assert.NotEmpty(logger.Warnings);
    }

    [Fact]
    public async Task WarnsOnPlayWhenTheProbeSizeLimiterIsNotInstalled()
    {
        bool originalStatus = ProbeSizeLimiterStatus.IsInstalled;
        ProbeSizeLimiterStatus.IsInstalled = false;
        try
        {
            var logger = new RecordingLogger<IlVodChannel>();
            FakeCatalog catalog = new() { StreamLink = ResolvedLink };

            await ChannelOver(catalog, logger).GetChannelItemMediaInfo("mako:shemesh-s1-e1", CancellationToken.None);

            Assert.Contains(logger.Warnings, message => message.Contains("NOT installed", StringComparison.Ordinal));
        }
        finally
        {
            ProbeSizeLimiterStatus.IsInstalled = originalStatus;
        }
    }

    [Fact]
    public async Task DoesNotWarnOnPlayWhenTheProbeSizeLimiterIsInstalled()
    {
        bool originalStatus = ProbeSizeLimiterStatus.IsInstalled;
        ProbeSizeLimiterStatus.IsInstalled = true;
        try
        {
            var logger = new RecordingLogger<IlVodChannel>();
            FakeCatalog catalog = new() { StreamLink = ResolvedLink };

            await ChannelOver(catalog, logger).GetChannelItemMediaInfo("mako:shemesh-s1-e1", CancellationToken.None);

            Assert.Empty(logger.Warnings);
        }
        finally
        {
            ProbeSizeLimiterStatus.IsInstalled = originalStatus;
        }
    }
}
