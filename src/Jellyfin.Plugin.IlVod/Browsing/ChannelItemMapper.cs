using Jellyfin.Plugin.IlVod.Model;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Model.Channels;

namespace Jellyfin.Plugin.IlVod.Browsing;

/// <summary>
/// Turns the plugin's own content records into the items Jellyfin shows in a channel.
/// </summary>
public static class ChannelItemMapper
{
    // Series and Season, not plain Container folders: clients such as Fladder have no screen
    // for a channel's plain folder ("ChannelFolderItem") but do for a series and a season.
    // Jellyfin's TV endpoints still call the plugin on demand for channel items, and Jellyfin
    // disables remote metadata fetchers for channel items, so mako's Hebrew titles are kept.
    private const ChannelFolderType ShowFolder = ChannelFolderType.Series;
    private const ChannelFolderType SeasonFolder = ChannelFolderType.Season;

    /// <summary>
    /// When the shape of an episode item last changed. Jellyfin re-saves an item it already
    /// holds only when this is newer than its copy, so bump it whenever <see cref="ToPlayable"/>
    /// starts sending something Jellyfin must persist (last: the placeholder source path).
    /// </summary>
    public static readonly DateTime EpisodeShapeRevision = new(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc);

    public static ChannelItemInfo ToFolder(Show show) => new()
    {
        Id = FolderId.ForShow(show.Id).ToString(),
        Name = show.Name,
        Type = ChannelItemType.Folder,
        FolderType = ShowFolder,
        ImageUrl = show.PosterUrl,
        Overview = show.Overview,
        Genres = show.Genres.ToList(),
    };

    public static ChannelItemInfo ToFolder(Season season, string showId) => new()
    {
        Id = FolderId.ForSeason(showId, season.Id).ToString(),
        Name = season.Name,
        Type = ChannelItemType.Folder,
        FolderType = SeasonFolder,
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
        RunTimeTicks = episode.Duration?.Ticks,
        DateModified = EpisodeShapeRevision,
        ImageUrl = episode.ThumbnailUrl,
        MediaSources = [MediaSourceFactory.CreatePlaceholder(episode.Id)],
    };
}
