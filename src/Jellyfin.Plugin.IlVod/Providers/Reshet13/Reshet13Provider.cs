using Jellyfin.Plugin.IlVod.Core;

namespace Jellyfin.Plugin.IlVod.Providers.Reshet13;

/// <summary>Reshet 13 as one of the channel's providers.</summary>
public sealed class Reshet13Provider : IContentProvider
{
    public Reshet13Provider(ICatalog catalog)
    {
        Catalog = catalog;
    }

    /// <summary>
    /// <see cref="ProviderInfo.ProbeSize"/> stays <c>null</c> (Jellyfin's own probe size) until it is
    /// verified on the phone. Reshet 13's streams carry a <c>timed_id3</c> data track that ffprobe
    /// does identify — unlike mako's, which forced mako's 20M limit — so the limit may not be needed.
    /// </summary>
    public static ProviderInfo Description { get; } = new(
        Id: "reshet13",
        DisplayName: "רשת 13",
        HomePageUrl: "https://13tv.co.il",
        ProbeSize: null,
        DefaultCategories: Reshet13Categories.Default);

    public ProviderInfo Info => Description;

    public ICatalog Catalog { get; }
}
