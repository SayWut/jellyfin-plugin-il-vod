namespace Jellyfin.Plugin.IlVod.Core;

/// <summary>
/// The codecs and fixed traits of a provider's streams, as Jellyfin needs them to copy a stream
/// instead of probing and re-encoding it. Each provider states its own; nothing here is assumed.
/// </summary>
public sealed record StreamFormat(
    string VideoCodec,
    string? PixelFormat,
    int? BitDepth,
    bool IsAvc,
    string? NalLengthSize,
    string AudioCodec,
    int? AudioChannels,
    int? AudioSampleRate,
    string? AudioProfile);
