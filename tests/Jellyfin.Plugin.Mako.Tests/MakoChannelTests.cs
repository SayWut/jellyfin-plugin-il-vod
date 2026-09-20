using Jellyfin.Plugin.Mako.Catalog;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.Mako.Tests;

public class MakoChannelTests
{
    private const string PastedUrl = "https://example.test/master.m3u8";
    private const string PastedContainer = "hls";

    private static MakoChannel ChannelOverStaticCatalog()
        => new(new StaticCatalog(() => PastedUrl, () => PastedContainer), NullLogger<MakoChannel>.Instance);

    private static Task<ChannelItemResult> Open(MakoChannel channel, string? folderId)
        => channel.GetChannelItems(new InternalChannelItemQuery { FolderId = folderId }, CancellationToken.None);

    [Fact]
    public async Task OpeningTheChannelListsTheShowsAsFolders()
    {
        ChannelItemResult result = await Open(ChannelOverStaticCatalog(), folderId: null);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.TotalRecordCount);
        Assert.All(result.Items, item => Assert.Equal(ChannelItemType.Folder, item.Type));
        Assert.Equal("show|shemesh", result.Items[0].Id);
    }

    [Fact]
    public async Task OpeningAShowListsItsSeasons()
    {
        ChannelItemResult result = await Open(ChannelOverStaticCatalog(), "show|shemesh");

        Assert.Equal(new[] { "season|shemesh-s1", "season|shemesh-s2" }, result.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task OpeningASeasonListsItsPlayableEpisodes()
    {
        ChannelItemResult result = await Open(ChannelOverStaticCatalog(), "season|shemesh-s1");

        Assert.Equal(3, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal(ChannelItemType.Media, item.Type));
    }

    [Fact]
    public async Task PressingPlayResolvesTheStreamAtThatMoment()
    {
        IEnumerable<MediaSourceInfo> sources = await ChannelOverStaticCatalog()
            .GetChannelItemMediaInfo("shemesh-s1-e1", CancellationToken.None);

        MediaSourceInfo source = Assert.Single(sources);
        Assert.Equal(PastedUrl, source.Path);
        Assert.Equal("shemesh-s1-e1", source.Id);
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
    public async Task PressingPlayWithNoPastedUrlThrowsStreamUnavailable()
    {
        var channel = new MakoChannel(new StaticCatalog(() => string.Empty, () => PastedContainer), NullLogger<MakoChannel>.Instance);

        await Assert.ThrowsAsync<StreamUnavailableException>(
            () => channel.GetChannelItemMediaInfo("shemesh-s1-e1", CancellationToken.None));
    }

    [Fact]
    public async Task OpeningAFolderWithAGarbageIdReturnsAnEmptyResultInsteadOfThrowing()
    {
        ChannelItemResult result = await Open(ChannelOverStaticCatalog(), "garbage");

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalRecordCount);
    }
}
