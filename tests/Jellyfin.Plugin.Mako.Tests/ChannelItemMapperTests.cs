using Jellyfin.Plugin.Mako.Browsing;
using Jellyfin.Plugin.Mako.Model;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Model.Channels;

namespace Jellyfin.Plugin.Mako.Tests;

public class ChannelItemMapperTests
{
    [Fact]
    public void ShowBecomesAFolderThatCarriesItsOwnMetadata()
    {
        var show = new Show("shemesh", "שמש", "https://img.test/p.jpg", "תיאור", ["קומדיה"]);

        ChannelItemInfo item = ChannelItemMapper.ToFolder(show);

        Assert.Equal(ChannelItemType.Folder, item.Type);
        Assert.Equal(ChannelFolderType.Container, item.FolderType);
        Assert.Equal("show|shemesh", item.Id);
        Assert.Equal("שמש", item.Name);
        Assert.Equal("https://img.test/p.jpg", item.ImageUrl);
        Assert.Equal("תיאור", item.Overview);
        Assert.Equal(new[] { "קומדיה" }, item.Genres);
    }

    [Fact]
    public void SeasonBecomesAFolderNumberedByItsSeason()
    {
        ChannelItemInfo item = ChannelItemMapper.ToFolder(new Season("shemesh-s2", 2, "עונה 2"));

        Assert.Equal(ChannelItemType.Folder, item.Type);
        Assert.Equal(ChannelFolderType.Container, item.FolderType);
        Assert.Equal("season|shemesh-s2", item.Id);
        Assert.Equal("עונה 2", item.Name);
        Assert.Equal(2, item.IndexNumber);
    }

    [Fact]
    public void EpisodeBecomesAPlayableVideoWithSeasonAndEpisodeNumbers()
    {
        var airDate = new DateTime(1997, 12, 25, 0, 0, 0, DateTimeKind.Utc);
        var episode = new Episode("shemesh-s1-e1", "שמש", 1, 1, "שמש בעל הנס", airDate);

        ChannelItemInfo item = ChannelItemMapper.ToPlayable(episode);

        Assert.Equal(ChannelItemType.Media, item.Type);
        Assert.Equal(ChannelMediaType.Video, item.MediaType);
        Assert.Equal(ChannelMediaContentType.Episode, item.ContentType);
        Assert.Equal("shemesh-s1-e1", item.Id);
        Assert.Equal("שמש בעל הנס", item.Name);
        Assert.Equal("שמש", item.SeriesName);
        Assert.Equal(1, item.ParentIndexNumber);
        Assert.Equal(1, item.IndexNumber);
        Assert.Equal(airDate, item.PremiereDate);
    }
}
