using Jellyfin.Plugin.Mako.Model;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.Mako.Browsing;

/// <summary>
/// Describes a resolved stream to Jellyfin. These flags decide whether Jellyfin plays
/// the stream directly or transcodes it, so they live in one place.
/// </summary>
public static class MediaSourceFactory
{
    /// <summary>
    /// The <see cref="MediaSourceInfo.Name"/> every mako media source carries. This is the one
    /// place that owns how the plugin recognises its own streams; nothing else should compare
    /// against the literal "mako".
    /// </summary>
    public const string SourceName = "mako";

    private const string VideoCodec = "h264";
    private const string AudioCodec = "aac";
    private const int VideoStreamIndex = 0;
    private const int AudioStreamIndex = 1;
    private const int StereoChannels = 2;

    // The value Jellyfin itself uses for live streams. Without it, a source with unknown
    // streams is analysed for up to 200 seconds before ffmpeg outputs anything.
    private const int AnalyzeDurationMilliseconds = 3000;

    // Id is left unset on purpose: Jellyfin assigns the item's own GUID, and its HLS
    // endpoint parses the media source id as a GUID. Any other value breaks playback.
    public static MediaSourceInfo Create(StreamLink link) => new()
    {
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
        AnalyzeDurationMs = AnalyzeDurationMilliseconds,
        MediaStreams = DescribeStreams(),
    };

    /// <summary>
    /// True when <paramref name="source"/> is a media source this plugin created.
    /// </summary>
    public static bool IsMakoSource(MediaSourceInfo? source) =>
        string.Equals(source?.Name, SourceName, StringComparison.Ordinal);

    // mako serves H.264 video with AAC audio. Telling Jellyfin so lets it copy the
    // streams instead of analysing, guessing and re-encoding them.
    private static MediaStream[] DescribeStreams() =>
    [
        new MediaStream { Type = MediaStreamType.Video, Codec = VideoCodec, Index = VideoStreamIndex },
        new MediaStream { Type = MediaStreamType.Audio, Codec = AudioCodec, Index = AudioStreamIndex, Channels = StereoChannels },
    ];
}
