namespace Jellyfin.Plugin.IlVod.Hls;

/// <summary>The highest-bandwidth variant of a master playlist, as far as it describes itself.</summary>
public sealed record HlsVariant(int? Width, int? Height, int? BitsPerSecond, string VideoProfile, double? VideoLevel);
