using Jellyfin.Plugin.IlVod.Aggregation;
using Jellyfin.Plugin.IlVod.Providers.Reshet13;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Reshet13;

public class Reshet13ProviderTests
{
    [Fact]
    public void DescribesReshet13UnderAValidProviderId()
    {
        Assert.Equal(("reshet13", "רשת 13", "https://13tv.co.il"), (Reshet13Provider.Description.Id, Reshet13Provider.Description.DisplayName, Reshet13Provider.Description.HomePageUrl));
        Assert.True(ProviderScopedId.IsValidProviderId(Reshet13Provider.Description.Id));
    }

    [Fact]
    public void StartsWithNoProbeSizeLimitAndReshet13sOwnCategories()
    {
        Assert.Null(Reshet13Provider.Description.ProbeSize);
        Assert.Same(Reshet13Categories.Default, Reshet13Provider.Description.DefaultCategories);
    }
}
