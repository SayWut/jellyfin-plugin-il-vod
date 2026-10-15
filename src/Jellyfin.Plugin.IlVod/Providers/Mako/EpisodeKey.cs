namespace Jellyfin.Plugin.IlVod.Providers.Mako;

/// <summary>
/// The three playback ids mako needs to build a playlist URL for one episode, packed into a
/// single string so an episode's Jellyfin id survives a restart with no extra lookup.
/// Safe as a separator because mako's own ids are <c>[0-9a-zA-Z]</c> only
/// (e.g. <c>d13f539206c35910VgnVCM200000650a10acRCRD</c>).
/// </summary>
public sealed record EpisodeKey(string VcmId, string ChannelId, string GalleryChannelId)
{
    private const char Separator = '~';
    private const int PartCount = 3;

    public override string ToString() => string.Join(Separator, VcmId, ChannelId, GalleryChannelId);

    /// <exception cref="FormatException">
    /// <paramref name="episodeId"/> is not exactly three non-empty parts joined by <see cref="Separator"/>.
    /// </exception>
    public static EpisodeKey Parse(string episodeId)
    {
        string[] parts = episodeId.Split(Separator);
        if (parts.Length != PartCount || parts.Any(string.IsNullOrEmpty))
        {
            throw new FormatException(
                $"mako episode id '{episodeId}' does not have exactly {PartCount} non-empty parts");
        }

        return new EpisodeKey(parts[0], parts[1], parts[2]);
    }
}
