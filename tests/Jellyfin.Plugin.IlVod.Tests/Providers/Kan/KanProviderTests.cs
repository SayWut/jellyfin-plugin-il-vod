using Jellyfin.Plugin.IlVod.Aggregation;
using Jellyfin.Plugin.IlVod.Providers.Kan;
using Jellyfin.Plugin.IlVod.Tests.Support;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Kan;

public class KanProviderTests
{
    [Fact]
    public void DescribesKanUnderAValidProviderId()
    {
        Assert.Equal(("kan", "כאן 11", "https://www.kan.org.il/lobby/kan-box/"), (KanProvider.Description.Id, KanProvider.Description.DisplayName, KanProvider.Description.HomePageUrl));
        Assert.True(ProviderScopedId.IsValidProviderId(KanProvider.Description.Id));
    }

    [Fact]
    public void LeavesTheProbeSizeUnlimitedAndUsesKansOwnCategories()
    {
        Assert.Null(KanProvider.Description.ProbeSize);
        Assert.Same(KanCategories.Default, KanProvider.Description.DefaultCategories);
    }

    [Fact]
    public void ItsInfoIsItsDescriptionAndItsCatalogTheOneItWasGiven()
    {
        FakeCatalog catalog = new();

        KanProvider provider = new(catalog);

        Assert.Same(KanProvider.Description, provider.Info);
        Assert.Same(catalog, provider.Catalog);
    }
}
