using System.Net;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Providers.Kan;
using Jellyfin.Plugin.IlVod.Tests.Support;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Kan;

public class KanCategoriesTests
{
    private static readonly Regex RailTitle = new(
        @"<h2 class=""h3 title-elem""[^>]*>\s*<a [^>]*>\s*([^<]+?)\s*</a>", RegexOptions.Compiled);

    [Fact]
    public void EveryGenreMapsIntoTheClosedSetOfSharedCategories()
    {
        Assert.All(KanCategories.Default.Values, shared => Assert.Contains(shared, SharedCategories.All));
    }

    [Fact]
    public void NoGenreIsMappedStraightToTheFallback()
    {
        Assert.DoesNotContain(SharedCategories.Fallback, KanCategories.Default.Values);
    }

    [Fact]
    public void EveryGenreInTheSavedLobbyHasAMapping()
    {
        string[] genres = [.. RailTitle.Matches(FixtureFile.ReadText("kan-lobby.html")).Select(match => WebUtility.HtmlDecode(match.Groups[1].Value))];

        Assert.Equal(24, genres.Length);
        Assert.All(genres, genre => Assert.True(KanCategories.Default.ContainsKey(genre), $"no mapping for '{genre}'"));
    }

    [Theory]
    [InlineData("סרטים", SharedCategories.Documentary)]
    [InlineData("לא מכאן: סדרות חו\"ל", SharedCategories.Drama)]
    [InlineData("מוזיקה", SharedCategories.Entertainment)]
    [InlineData("תוכניות אקטואליה", SharedCategories.CurrentAffairs)]
    public void TheAgreedMappingHolds(string genre, string shared)
    {
        Assert.Equal(shared, KanCategories.Default[genre]);
    }
}
