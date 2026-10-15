using Jellyfin.Plugin.IlVod.Core;

namespace Jellyfin.Plugin.IlVod.Tests.Support;

/// <summary>A provider over any <see cref="ICatalog"/> (usually a <see cref="FakeCatalog"/>), for
/// the tests of what sits above every provider.</summary>
public sealed class FakeProvider : IContentProvider
{
    public FakeProvider(string id, ICatalog catalog, IReadOnlyDictionary<string, string>? defaultCategories = null, string? probeSize = null)
    {
        Info = new ProviderInfo(id, $"{id} (fake)", $"https://{id}.test", probeSize, defaultCategories ?? new Dictionary<string, string>());
        Catalog = catalog;
    }

    public ProviderInfo Info { get; }

    public ICatalog Catalog { get; }
}
