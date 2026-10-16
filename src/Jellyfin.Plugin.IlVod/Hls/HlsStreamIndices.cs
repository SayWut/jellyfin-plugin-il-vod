namespace Jellyfin.Plugin.IlVod.Hls;

/// <summary>The input indices ffmpeg gives a master playlist's best video and its audio.</summary>
public sealed record HlsStreamIndices(int Video, int Audio);
