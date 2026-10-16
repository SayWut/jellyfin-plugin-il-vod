using Jellyfin.Plugin.IlVod.Core;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.IlVod.Browsing;

/// <summary>
/// Describes a resolved stream to Jellyfin. These flags decide whether Jellyfin plays
/// the stream directly or transcodes it, so they live in one place.
/// </summary>
public static class MediaSourceFactory
{
    /// <summary>
    /// What every media source this plugin creates is named, followed by its provider's id
    /// (<c>il-vod/mako</c>). This is the one place that owns how the plugin recognises its own
    /// streams and which provider each came from; nothing else should parse the name.
    /// </summary>
    private const string SourceNamePrefix = "il-vod/";

    // The value Jellyfin itself uses for live streams. Without it, a source with unknown
    // streams is analysed for up to 200 seconds before ffmpeg outputs anything.
    private const int AnalyzeDurationMilliseconds = 3000;

    // A directory that does not exist: Jellyfin judges "on disk" from the text of a path
    // and never opens it, and nothing must ever be found there.
    private const string PlaceholderDirectory = "/nonexistent/jellyfin-plugin-il-vod";
    private const string PlaceholderExtension = ".mkv";

    // Id is left unset on purpose: Jellyfin assigns the item's own GUID, and its HLS
    // endpoint parses the media source id as a GUID. Any other value breaks playback.
    public static MediaSourceInfo Create(StreamLink link, string providerId) => new()
    {
        Name = SourceNamePrefix + providerId,
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
    /// The id of the provider whose stream <paramref name="source"/> is, when this plugin created
    /// it; <c>null</c> for every other source. Checks more than the name: Jellyfin names a local
    /// library file's media source after its file name, so a file on disk that happens to be
    /// called "il-vod/mako" must not match — only a remote HTTP source, which is all this plugin
    /// ever produces, can be ours.
    /// </summary>
    public static string? ProviderIdOf(MediaSourceInfo? source) =>
        source is not null
        && source.Name is { } name
        && name.StartsWith(SourceNamePrefix, StringComparison.Ordinal)
        && source.Protocol == MediaProtocol.Http
        && source.IsRemote
            ? name[SourceNamePrefix.Length..]
            : null;

    // Telling Jellyfin the codecs up front lets it copy the streams instead of analysing,
    // guessing and re-encoding them. A provider that cannot name them gets none described.
    private static MediaStream[] DescribeStreams(StreamDescription description) =>
        description.Format is { } format
            ? InFfmpegOrder(DescribeVideoStream(description, format), DescribeAudioStream(description, format))
            : [];

    /// <summary>
    /// Jellyfin tells ffmpeg which input stream to map by its position in this list
    /// (<c>EncodingHelper.FindIndex</c>), never by its <c>Index</c>. So each stream is placed at its
    /// own index, and any track in between — another variant of the master playlist — is filled
    /// with a data stream, a type Jellyfin never picks to play.
    /// </summary>
    private static MediaStream[] InFfmpegOrder(MediaStream video, MediaStream audio)
    {
        var streams = new MediaStream[Math.Max(video.Index, audio.Index) + 1];
        streams[video.Index] = video;
        streams[audio.Index] = audio;
        for (int index = 0; index < streams.Length; index++)
        {
            streams[index] ??= new MediaStream { Type = MediaStreamType.Data, Index = index };
        }

        return streams;
    }

    private static MediaStream DescribeVideoStream(StreamDescription description, StreamFormat format) => new()
    {
        Type = MediaStreamType.Video,
        Codec = format.VideoCodec,
        Index = description.VideoStreamIndex,
        Width = description.Width,
        Height = description.Height,
        BitRate = description.BitsPerSecond,
        Profile = description.VideoProfile,
        Level = description.VideoLevel,
        IsInterlaced = false,
        PixelFormat = format.PixelFormat,
        BitDepth = format.BitDepth,
        IsAVC = format.IsAvc,
        NalLengthSize = format.NalLengthSize,
    };

    private static MediaStream DescribeAudioStream(StreamDescription description, StreamFormat format) => new()
    {
        Type = MediaStreamType.Audio,
        Codec = format.AudioCodec,
        Index = description.AudioStreamIndex,
        Channels = format.AudioChannels,
        SampleRate = format.AudioSampleRate,
        Profile = format.AudioProfile,
    };
}
