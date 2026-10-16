using Jellyfin.Plugin.IlVod.Core;

namespace Jellyfin.Plugin.IlVod.Providers.Kan;

/// <summary>Kan 11 as one of the channel's providers.</summary>
public sealed class KanProvider : IContentProvider
{
    public KanProvider(ICatalog catalog)
    {
        Catalog = catalog;
    }

    /// <summary>
    /// No probe-size limit: the limit mako and Reshet 13 need is for a <c>timed_id3</c> data track
    /// ffmpeg keeps buffering, and Kan's streams carry none (ffprobe, 2026-10-16: AAC audio and H.264
    /// only). Set one if a measurement on the phone ever shows ffmpeg's memory growing.
    /// </summary>
    public static ProviderInfo Description { get; } = new(
        Id: "kan",
        DisplayName: "כאן 11",
        HomePageUrl: "https://www.kan.org.il/lobby/kan-box/",
        ProbeSize: null,
        DefaultCategories: KanCategories.Default);

    public ProviderInfo Info => Description;

    public ICatalog Catalog { get; }
}
