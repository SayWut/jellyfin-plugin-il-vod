using Jellyfin.Plugin.IlVod.Core;

namespace Jellyfin.Plugin.IlVod.Providers.Kan;

/// <summary>What every Kan stream is, whatever its resolution — measured with ffprobe on Kan's
/// Redge master playlists on 2026-10-16.</summary>
public static class KanStreams
{
    /// <summary>
    /// 8-bit planar H.264 in fragmented MP4 segments, so a length-prefixed bitstream
    /// (<c>is_avc=true</c>, 4-byte lengths), with stereo AAC-LC audio at 48 kHz.
    /// </summary>
    public static StreamFormat Format { get; } = new(
        VideoCodec: "h264",
        PixelFormat: "yuv420p",
        BitDepth: 8,
        IsAvc: true,
        NalLengthSize: "4",
        AudioCodec: "aac",
        AudioChannels: 2,
        AudioSampleRate: 48000,
        AudioProfile: "LC");
}
