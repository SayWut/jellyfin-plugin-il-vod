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

    // mako serves planar 8-bit H.264 in an MP4-style (length-prefixed, not Annex B) bitstream;
    // telling Jellyfin so up front lets it copy the video instead of probing for these.
    private const string VideoPixelFormat = "yuv420p";
    private const int VideoBitDepth = 8;
    private const string VideoNalLengthSize = "0";

    // mako serves AAC-LC audio at a fixed sample rate.
    private const int AudioSampleRate = 48000;
    private const string AudioProfile = "LC";

    // The value Jellyfin itself uses for live streams. Without it, a source with unknown
    // streams is analysed for up to 200 seconds before ffmpeg outputs anything.
    private const int AnalyzeDurationMilliseconds = 3000;

    // A directory that does not exist: Jellyfin judges "on disk" from the text of a path
    // and never opens it, and nothing must ever be found there.
    private const string PlaceholderDirectory = "/nonexistent/jellyfin-plugin-mako";
    private const string PlaceholderExtension = ".mkv";

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
        RunTimeTicks = link.Description.Duration?.Ticks,
        SupportsDirectPlay = true,
        SupportsDirectStream = true,
        SupportsTranscoding = true,
        SupportsProbing = true,
        RequiresOpening = false,
        AnalyzeDurationMs = AnalyzeDurationMilliseconds,
        MediaStreams = DescribeStreams(link.Description),
    };

    /// <summary>
    /// A stand-in source given to Jellyfin at browse time. Its file-like path makes Jellyfin
    /// report the episode as being on disk, which some clients (Fladder) require before they
    /// allow playback. Because it is a <see cref="MediaSourceType.Placeholder"/>, Jellyfin
    /// neither probes it nor offers it to clients: at play time they only ever receive the
    /// real stream from <see cref="Create"/>.
    /// </summary>
    public static MediaSourceInfo CreatePlaceholder(string episodeId) => new()
    {
        Path = $"{PlaceholderDirectory}/{episodeId}{PlaceholderExtension}",
        Protocol = MediaProtocol.File,
        Type = MediaSourceType.Placeholder,
        IsRemote = false,
    };

    /// <summary>
    /// True when <paramref name="source"/> is a media source this plugin created. Checks more
    /// than the name: Jellyfin names a local library file's media source after its file name, so
    /// a file on disk that happens to be called "mako" must not match — only a remote HTTP
    /// source, which is all this plugin ever produces, can be ours.
    /// </summary>
    public static bool IsMakoSource(MediaSourceInfo? source) =>
        source is not null
        && string.Equals(source.Name, SourceName, StringComparison.Ordinal)
        && source.Protocol == MediaProtocol.Http
        && source.IsRemote;

    // mako serves H.264 video with AAC audio. Telling Jellyfin so lets it copy the
    // streams instead of analysing, guessing and re-encoding them.
    private static MediaStream[] DescribeStreams(StreamDescription description) =>
    [
        DescribeVideoStream(description),
        DescribeAudioStream(),
    ];

    private static MediaStream DescribeVideoStream(StreamDescription description) => new()
    {
        Type = MediaStreamType.Video,
        Codec = VideoCodec,
        Index = VideoStreamIndex,
        Width = description.Width,
        Height = description.Height,
        BitRate = description.BitsPerSecond,
        Profile = description.VideoProfile,
        Level = description.VideoLevel,
        IsInterlaced = false,
        PixelFormat = VideoPixelFormat,
        BitDepth = VideoBitDepth,
        IsAVC = true,
        NalLengthSize = VideoNalLengthSize,
    };

    private static MediaStream DescribeAudioStream() => new()
    {
        Type = MediaStreamType.Audio,
        Codec = AudioCodec,
        Index = AudioStreamIndex,
        Channels = StereoChannels,
        SampleRate = AudioSampleRate,
        Profile = AudioProfile,
    };
}
