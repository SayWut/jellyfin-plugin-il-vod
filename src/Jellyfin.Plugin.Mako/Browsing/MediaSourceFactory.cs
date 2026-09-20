using Jellyfin.Plugin.Mako.Model;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.Mako.Browsing;

/// <summary>
/// Describes a resolved stream to Jellyfin. These flags decide whether Jellyfin plays
/// the stream directly or transcodes it, so they live in one place.
/// </summary>
public static class MediaSourceFactory
{
    private const string SourceName = "mako";

    public static MediaSourceInfo Create(string episodeId, StreamLink link) => new()
    {
        Id = episodeId,
        Name = SourceName,
        Path = link.Url,
        Protocol = MediaProtocol.Http,
        Container = string.IsNullOrEmpty(link.Container) ? null : link.Container,
        IsRemote = true,
        IsInfiniteStream = false,
        SupportsDirectPlay = true,
        SupportsDirectStream = true,
        SupportsTranscoding = true,
        SupportsProbing = true,
        RequiresOpening = false,
    };
}
