using Jellyfin.Plugin.IlVod.Core;

namespace Jellyfin.Plugin.IlVod.Providers.Mako;

/// <summary>mako 12+ as one of the channel's providers.</summary>
public sealed class MakoProvider : IContentProvider
{
    /// <summary>
    /// mako's streams carry a data track ffmpeg can never identify, so Jellyfin's global
    /// <c>-probesize 1G</c> makes it buffer the whole episode in RAM; 20M is large enough to
    /// identify H.264 and AAC from the first segments (measured: 1G -> ~440 MB RSS on a 200 MB
    /// stream; 5M -> 34 MB). See docs/spec.md §4.3.
    /// </summary>
    private const string ProbeSize = "20M";

    public MakoProvider(ICatalog catalog)
    {
        Catalog = catalog;
    }

    public static ProviderInfo Description { get; } = new(
        Id: "mako",
        DisplayName: "mako 12+",
        HomePageUrl: "https://www.mako.co.il/mako-vod",
        ProbeSize: ProbeSize);

    public ProviderInfo Info => Description;

    public ICatalog Catalog { get; }

    public IReadOnlyDictionary<string, string> DefaultCategories { get; } = new Dictionary<string, string>();
}
