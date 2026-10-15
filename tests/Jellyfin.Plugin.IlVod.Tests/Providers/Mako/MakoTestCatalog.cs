using Jellyfin.Plugin.IlVod.Cache;
using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Providers.Mako;
using Jellyfin.Plugin.IlVod.Providers.Mako.Source;
using Jellyfin.Plugin.IlVod.Providers.Mako.Transform;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Mako;

/// <summary>mako's catalogue exactly as the plugin composes it — <see cref="MakoSource"/> and
/// <see cref="MakoTransformer"/> joined by <see cref="SourceCatalog{TIndex,TShowPage,TSeasonPage,TStream}"/> —
/// over the default lifetimes, for the tests that drive mako end to end through <see cref="ICatalog"/>.</summary>
internal static class MakoTestCatalog
{
    public static ICatalog Over(MakoPageSource pages, StreamResolver resolver, TimedCache streamLinkCache) =>
        new SourceCatalog<MakoShowIndex, ShowPage, MakoSeasonPage, MakoResolvedStream>(
            new MakoSource(pages, resolver, () => CatalogueLifetimes.Default, NullLogger<MakoSource>.Instance),
            new MakoTransformer(),
            streamLinkCache,
            () => CatalogueLifetimes.Default.StreamLink);
}
