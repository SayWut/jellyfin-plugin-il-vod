using Jellyfin.Plugin.Mako.Catalog;
using Jellyfin.Plugin.Mako.MakoApi;
using Jellyfin.Plugin.Mako.Model;
using Jellyfin.Plugin.Mako.Tests.Support;
using Jellyfin.Plugin.Mako.Tests.Transcoding;
using Jellyfin.Plugin.Mako.Transcoding;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.Mako.Tests;

[Collection(ProbeSizeLimiterStatusCollection.Name)]
public class MakoChannelTests
{
    private const string ResolvedUrl = "https://example.test/master.m3u8";
    private const string ResolvedContainer = "hls";

    private static readonly StreamLink ResolvedLink = new(ResolvedUrl, ResolvedContainer, StreamDescription.MakoDefault);

    private static MakoChannel ChannelOver(FakeCatalog catalog) => ChannelOver(catalog, NullLogger<MakoChannel>.Instance);

    /// <summary>The catalogue reaches the channel as a <see cref="Lazy{T}"/> so constructing the
    /// channel touches nothing — see <c>PluginServiceRegistratorTests</c> for why.</summary>
    private static MakoChannel ChannelOver(FakeCatalog catalog, ILogger<MakoChannel> logger) =>
        new(new Lazy<ICatalog>(() => catalog), logger);

    private static Task<ChannelItemResult> Open(MakoChannel channel, string? folderId)
        => channel.GetChannelItems(new InternalChannelItemQuery { FolderId = folderId }, CancellationToken.None);

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

        Assert.Equal(new[] { "series-season|shemesh-s1", "series-season|shemesh-s2" }, result.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task OpeningASeasonListsItsPlayableEpisodes()
    {
        ChannelItemResult result = await Open(ChannelOver(new FakeCatalog()), "series-season|shemesh-s1");

        Assert.Equal(3, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal(ChannelItemType.Media, item.Type));
    }

    [Fact]
    public async Task PressingPlayResolvesTheStreamAtThatMoment()
    {
        FakeCatalog catalog = new() { StreamLink = ResolvedLink };

        IEnumerable<MediaSourceInfo> sources = await ChannelOver(catalog).GetChannelItemMediaInfo("shemesh-s1-e1", CancellationToken.None);

        MediaSourceInfo source = Assert.Single(sources);
        Assert.Equal(ResolvedUrl, source.Path);
    }

    [Fact]
    public void DoesNotAskJellyfinToCrawlItForLatestItems()
    {
        Assert.False(typeof(ISupportsLatestMedia).IsAssignableFrom(typeof(MakoChannel)));
    }

    [Fact]
    public void DoesNotAskJellyfinToProbeTheStreamAtBrowseTime()
    {
        Assert.False(typeof(ISupportsMediaProbe).IsAssignableFrom(typeof(MakoChannel)));
    }

    [Fact]
    public async Task PressingPlayWithNoResolvableStreamPropagatesStreamUnavailable()
    {
        FakeCatalog catalog = new() { StreamLinkFailure = new StreamUnavailableException("no stream available") };

        await Assert.ThrowsAsync<StreamUnavailableException>(
            () => ChannelOver(catalog).GetChannelItemMediaInfo("shemesh-s1-e1", CancellationToken.None));
    }

    [Fact]
    public async Task OpeningAFolderWithAGarbageIdReturnsAnEmptyResultInsteadOfThrowing()
    {
        ChannelItemResult result = await Open(ChannelOver(new FakeCatalog()), "garbage");

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalRecordCount);
    }

    [Fact]
    public async Task ABotChallengeWhileBrowsingShowsAnEmptyFolderInsteadOfFailing()
    {
        FakeCatalog catalog = new() { BrowseFailure = new BotChallengeException("https://www.mako.co.il/mako-vod/shemesh?query=secret") };
        var logger = new RecordingLogger<MakoChannel>();

        ChannelItemResult result = await Open(ChannelOver(catalog, logger), folderId: null);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalRecordCount);
        Assert.Contains(logger.Warnings, message => message.Contains("bot challenge", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logger.Lines, line => line.Contains("query=secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AMakoRequestFailureWhileBrowsingAShowShowsAnEmptyFolderInsteadOfFailing()
    {
        FakeCatalog catalog = new() { BrowseFailure = new MakoRequestException("HTTP 500 for www.mako.co.il/mako-vod/shemesh") };
        var logger = new RecordingLogger<MakoChannel>();

        ChannelItemResult result = await Open(ChannelOver(catalog, logger), "series|shemesh");

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
            var logger = new RecordingLogger<MakoChannel>();
            FakeCatalog catalog = new() { StreamLink = ResolvedLink };

            await ChannelOver(catalog, logger).GetChannelItemMediaInfo("shemesh-s1-e1", CancellationToken.None);

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
            var logger = new RecordingLogger<MakoChannel>();
            FakeCatalog catalog = new() { StreamLink = ResolvedLink };

            await ChannelOver(catalog, logger).GetChannelItemMediaInfo("shemesh-s1-e1", CancellationToken.None);

            Assert.Empty(logger.Warnings);
        }
        finally
        {
            ProbeSizeLimiterStatus.IsInstalled = originalStatus;
        }
    }
}
