using Jellyfin.Plugin.IlVod.Core;

namespace Jellyfin.Plugin.IlVod.Providers.Mako;

/// <summary>What every mako stream is, whatever its resolution: H.264 video and AAC-LC audio.</summary>
public static class MakoStreams
{
    /// <summary>The H.264 profile assumed when a master playlist names no <c>CODECS</c>.</summary>
    public const string UnmeasuredProfile = "Main";

    /// <summary>
    /// mako serves planar 8-bit H.264 in an MP4-style (length-prefixed, not Annex B) bitstream,
    /// with stereo AAC-LC audio at a fixed 48 kHz.
    /// </summary>
    public static StreamFormat Format { get; } = new(
        VideoCodec: "h264",
        PixelFormat: "yuv420p",
        BitDepth: 8,
        IsAvc: true,
        NalLengthSize: "0",
        AudioCodec: "aac",
        AudioChannels: 2,
        AudioSampleRate: 48000,
        AudioProfile: "LC");

    /// <summary>
    /// What mako's own streams were measured as when no better information is available
    /// (no master playlist variant, or one with no <c>CODECS</c> attribute).
    /// </summary>
    public static StreamDescription DefaultDescription { get; } = new(null, null, null, UnmeasuredProfile, null) { Format = Format };
}
