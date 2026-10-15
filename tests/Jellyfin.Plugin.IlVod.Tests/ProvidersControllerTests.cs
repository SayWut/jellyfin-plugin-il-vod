using Jellyfin.Plugin.IlVod.Api;
using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Providers.Mako;

namespace Jellyfin.Plugin.IlVod.Tests;

public class ProvidersControllerTests
{
    [Fact]
    public void ListsEveryProviderWithItsDefaultCategoriesAndTheSharedNames()
    {
        ProvidersController controller = new([MakoProvider.Description]);

        ProvidersController.ProvidersResponse response = controller.GetProviders().Value!;

        ProvidersController.ProviderDto mako = Assert.Single(response.Providers);
        Assert.Equal(("mako", "mako 12+"), (mako.Id, mako.DisplayName));
        Assert.Equal(MakoCategories.Default, mako.DefaultCategories);
        Assert.Equal(SharedCategories.All, response.SharedCategories);
    }
}
