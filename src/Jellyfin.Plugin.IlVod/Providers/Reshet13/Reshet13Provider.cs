using Jellyfin.Plugin.IlVod.Core;

namespace Jellyfin.Plugin.IlVod.Providers.Reshet13;

/// <summary>Reshet 13 as one of the channel's providers.</summary>
public sealed class Reshet13Provider : IContentProvider
{
    /// <summary>
    /// Reshet 13's streams carry a <c>timed_id3</c> data track, and under Jellyfin's global
    /// <c>-probesize 1G</c> ffmpeg kept buffering it, as it does mako's (measured on the phone: RSS
    /// grew ~1.3 MB/s while remuxing). 20M, mako's limit, identifies H.264 and AAC from the first segments.
    /// </summary>
    private const string ProbeSize = "20M";

    public Reshet13Provider(ICatalog catalog)
    {
        Catalog = catalog;
    }

    public static ProviderInfo Description { get; } = new(
        Id: "reshet13",
        DisplayName: "רשת 13",
        HomePageUrl: "https://13tv.co.il",
        ProbeSize: ProbeSize,
        DefaultCategories: Reshet13Categories.Default);

    public ProviderInfo Info => Description;

    public ICatalog Catalog { get; }
}
