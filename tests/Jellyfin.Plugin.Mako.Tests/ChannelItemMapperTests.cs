using Jellyfin.Plugin.Mako.Browsing;
using Jellyfin.Plugin.Mako.Model;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.Mako.Tests;

public class ChannelItemMapperTests
{
    [Fact]
    public void ShowBecomesAFolderThatCarriesItsOwnMetadata()
    {
        var show = new Show("shemesh", "שמש", "https://img.test/p.jpg", "תיאור", ["קומדיה"]);

        ChannelItemInfo item = ChannelItemMapper.ToFolder(show);

        Assert.Equal(ChannelItemType.Folder, item.Type);
        Assert.Equal(ChannelFolderType.Series, item.FolderType);
        Assert.Equal("series|shemesh", item.Id);
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
        Assert.Equal(ChannelFolderType.Season, item.FolderType);
        Assert.Equal("series-season|shemesh-s2", item.Id);
        Assert.Equal("עונה 2", item.Name);
        Assert.Equal(2, item.IndexNumber);
    }

    [Fact]
    public void EpisodeBecomesAPlayableVideoWithSeasonAndEpisodeNumbers()
    {
        var airDate = new DateTime(1997, 12, 25, 0, 0, 0, DateTimeKind.Utc);
        var episode = new Episode("shemesh-s1-e1", "שמש", 1, 1, "שמש בעל הנס", airDate, TimeSpan.FromMinutes(25), ThumbnailUrl: null);

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
        Assert.Equal(TimeSpan.FromMinutes(25).Ticks, item.RunTimeTicks);
    }

    [Fact]
    public void EpisodeWithNoDurationHasNoRunTimeTicks()
    {
        var episode = new Episode("shemesh-s1-e1", "שמש", 1, 1, "שמש בעל הנס", AirDate: null, Duration: null, ThumbnailUrl: null);

        ChannelItemInfo item = ChannelItemMapper.ToPlayable(episode);

        Assert.Null(item.RunTimeTicks);
    }

    [Fact]
    public void EpisodeCarriesAPlaceholderFilePathSoClientsSeeItAsOnDisk()
    {
        var episode = new Episode("shemesh-s1-e1", "שמש", 1, 1, "שמש בעל הנס", AirDate: null, Duration: null, ThumbnailUrl: null);

        ChannelItemInfo item = ChannelItemMapper.ToPlayable(episode);

        MediaSourceInfo placeholder = Assert.Single(item.MediaSources);
        Assert.Equal(MediaSourceType.Placeholder, placeholder.Type);
        Assert.Equal(MediaProtocol.File, placeholder.Protocol);
        Assert.False(placeholder.IsRemote);
        Assert.DoesNotContain("://", placeholder.Path, StringComparison.Ordinal);
        Assert.EndsWith("shemesh-s1-e1.mkv", placeholder.Path, StringComparison.Ordinal);
        Assert.False(File.Exists(placeholder.Path));
    }

    [Fact]
    public void PlaceholderIsNeverMistakenForAMakoStream()
    {
        MediaSourceInfo placeholder = MediaSourceFactory.CreatePlaceholder("shemesh-s1-e1");

        Assert.False(MediaSourceFactory.IsMakoSource(placeholder));
    }

    [Fact]
    public void EpisodeIsStampedWithTheShapeRevisionSoJellyfinReSavesOlderCopies()
    {
        var episode = new Episode("shemesh-s1-e1", "שמש", 1, 1, "שמש בעל הנס", AirDate: null, Duration: null, ThumbnailUrl: null);

        ChannelItemInfo item = ChannelItemMapper.ToPlayable(episode);

        Assert.Equal(ChannelItemMapper.EpisodeShapeRevision, item.DateModified);
    }
}
