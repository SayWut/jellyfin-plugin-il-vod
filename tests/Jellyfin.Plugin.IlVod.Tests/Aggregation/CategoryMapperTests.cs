using Jellyfin.Plugin.IlVod.Aggregation;
using Jellyfin.Plugin.IlVod.Configuration;
using Jellyfin.Plugin.IlVod.Core;

namespace Jellyfin.Plugin.IlVod.Tests.Aggregation;

public class CategoryMapperTests
{
    private static readonly ProviderInfo Provider = new(
        "alpha",
        "Alpha",
        "https://alpha.test",
        ProbeSize: null,
        DefaultCategories: new Dictionary<string, string> { ["דוקו"] = SharedCategories.Documentary, ["סרטי תעודה"] = SharedCategories.Documentary });

    [Fact]
    public void ADefaultMappingRenamesAndAnUnmappedNameIsKeptAsIs()
    {
        CategoryMapper mapper = new(() => []);

        Assert.Equal([SharedCategories.Documentary, "אחר"], mapper.Map(Provider, ["דוקו", "אחר"]));
    }

    [Fact]
    public void AnOverrideWinsOverTheDefaultForItsOwnProviderOnly()
    {
        CategoryMapper mapper = new(() =>
        [
            new CategoryOverride { ProviderId = "alpha", SourceName = "דוקו", SharedName = "תיעודי" },
            new CategoryOverride { ProviderId = "beta", SourceName = "אחר", SharedName = SharedCategories.Comedy },
        ]);

        Assert.Equal(["תיעודי", "אחר"], mapper.Map(Provider, ["דוקו", "אחר"]));
    }

    [Fact]
    public void TwoNamesMappingToOneSharedCategoryAreListedOnce()
    {
        CategoryMapper mapper = new(() => []);

        Assert.Equal([SharedCategories.Documentary], mapper.Map(Provider, ["דוקו", "סרטי תעודה"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ARowWithNoSharedNameIsIgnoredRatherThanBlankingTheCategory(string sharedName)
    {
        CategoryMapper mapper = new(() => [new CategoryOverride { ProviderId = "alpha", SourceName = "דוקו", SharedName = sharedName }]);

        Assert.Equal([SharedCategories.Documentary], mapper.Map(Provider, ["דוקו"]));
    }
}
