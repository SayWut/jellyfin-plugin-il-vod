using System.Text.Json;
using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Providers.Reshet13;
using Jellyfin.Plugin.IlVod.Tests.Support;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Reshet13;

public class Reshet13CategoriesTests
{
    [Fact]
    public void EveryGenreMapsIntoTheClosedSetOfSharedCategories()
    {
        Assert.All(Reshet13Categories.Default.Values, shared => Assert.Contains(shared, SharedCategories.All));
    }

    [Fact]
    public void NoGenreIsMappedStraightToTheFallback()
    {
        Assert.DoesNotContain(SharedCategories.Fallback, Reshet13Categories.Default.Values);
    }

    [Fact]
    public void EveryGenreInTheSavedCatalogueHasAMapping()
    {
        using JsonDocument series = JsonDocument.Parse(FixtureFile.ReadText("reshet13-series.json"));
        IEnumerable<string> genres = series.RootElement.GetProperty("result").GetProperty("objects").EnumerateArray()
            .SelectMany(asset => asset.GetProperty("tags").GetProperty("Genre").GetProperty("objects").EnumerateArray())
            .Select(genre => genre.GetProperty("value").GetString()!);

        Assert.All(genres, genre => Assert.True(Reshet13Categories.Default.ContainsKey(genre), $"no mapping for '{genre}'"));
    }
}
