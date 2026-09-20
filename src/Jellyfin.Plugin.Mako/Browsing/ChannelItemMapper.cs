using Jellyfin.Plugin.Mako.Model;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Model.Channels;

namespace Jellyfin.Plugin.Mako.Browsing;

/// <summary>
/// Turns the plugin's own content records into the items Jellyfin shows in a channel.
/// </summary>
public static class ChannelItemMapper
{
    // Container, not Series/Season: a Series folder makes Jellyfin run its metadata
    // providers, which would rename mako's Hebrew titles to TMDb matches.
    private const ChannelFolderType PlainFolder = ChannelFolderType.Container;

    public static ChannelItemInfo ToFolder(Show show) => new()
    {
        Id = FolderId.ForShow(show.Id).ToString(),
        Name = show.Name,
        Type = ChannelItemType.Folder,
        FolderType = PlainFolder,
        ImageUrl = show.PosterUrl,
        Overview = show.Overview,
        Genres = show.Genres.ToList(),
    };

    public static ChannelItemInfo ToFolder(Season season) => new()
    {
        Id = FolderId.ForSeason(season.Id).ToString(),
        Name = season.Name,
        Type = ChannelItemType.Folder,
        FolderType = PlainFolder,
        IndexNumber = season.Number,
    };

    public static ChannelItemInfo ToPlayable(Episode episode) => new()
    {
        Id = episode.Id,
        Name = episode.Name,
        Type = ChannelItemType.Media,
        MediaType = ChannelMediaType.Video,
        ContentType = ChannelMediaContentType.Episode,
        SeriesName = episode.ShowName,
        ParentIndexNumber = episode.SeasonNumber,
        IndexNumber = episode.Number,
        PremiereDate = episode.AirDate,
    };
}
