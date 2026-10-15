using Jellyfin.Plugin.IlVod.Core;

namespace Jellyfin.Plugin.IlVod.Providers.Reshet13;

/// <summary>What every Reshet 13 stream is, whatever its resolution — measured with ffprobe on the
/// clean Kaltura manifest on 2026-10-16.</summary>
public static class Reshet13Streams
{
    /// <summary>
    /// 8-bit planar H.264 in MPEG-TS segments, so an Annex B bitstream (<c>is_avc=false</c>), with
    /// stereo AAC-LC audio at 44.1 kHz.
    /// </summary>
    public static StreamFormat Format { get; } = new(
        VideoCodec: "h264",
        PixelFormat: "yuv420p",
        BitDepth: 8,
        IsAvc: false,
        NalLengthSize: "0",
        AudioCodec: "aac",
        AudioChannels: 2,
        AudioSampleRate: 44100,
        AudioProfile: "LC");
}
