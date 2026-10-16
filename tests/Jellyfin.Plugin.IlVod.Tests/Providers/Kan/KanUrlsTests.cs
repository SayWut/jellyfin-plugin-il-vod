using Jellyfin.Plugin.IlVod.Providers.Kan.Source;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Kan;

public class KanUrlsTests
{
    private const string DramaSlug = "%D7%93%D7%A8%D7%9E%D7%94";

    [Fact]
    public void TheLobbyIsKanBoxOnTheAppHost() =>
        Assert.Equal("https://mobapi.kan.org.il/lobby/kan-box/", KanUrls.Lobby);

    [Fact]
    public void AGenrePageKeepsItsSlugEncodedAsTheLobbyGaveIt() =>
        Assert.Equal(
            $"https://mobapi.kan.org.il/tags/content-category-tags/{DramaSlug}/?page=2",
            KanUrls.GenrePage(DramaSlug, 2));

    [Theory]
    [InlineData("")]
    [InlineData("../api")]
    [InlineData("drama?x=1")]
    [InlineData("דרמה")]
    public void AGenreSlugThatIsNotAnEncodedPathSegmentIsRefused(string slug)
    {
        Assert.False(KanUrls.IsGenreSlug(slug));
        Assert.Throws<ArgumentException>(() => KanUrls.GenrePage(slug, 1));
    }

    [Fact]
    public void GenrePagesCountFromOne() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => KanUrls.GenrePage(DramaSlug, 0));

    [Fact]
    public void AProgramFeedIsAskedForByItsId() =>
        Assert.Equal("https://mobapi.kan.org.il/api/mobile/program?id=11510", KanUrls.ProgramFeed("11510"));

    [Fact]
    public void AnEpisodePageLivesUnderItsProgram() =>
        Assert.Equal(
            "https://mobapi.kan.org.il/content/kan/kan-11/p-11510/1107310/",
            KanUrls.EpisodePage("11510", "1107310"));

    [Theory]
    [InlineData("11510&id=1")]
    [InlineData("")]
    [InlineData("1 2")]
    public void AnIdThatIsNotDigitsIsRefused(string id)
    {
        Assert.Throws<ArgumentException>(() => KanUrls.ProgramFeed(id));
        Assert.Throws<ArgumentException>(() => KanUrls.EpisodePage(id, "1107310"));
        Assert.Throws<ArgumentException>(() => KanUrls.EpisodePage("11510", id));
    }

    [Theory]
    [InlineData("https://www.kan.org.il/content/kan/kan-11/p-13291/", true)]
    [InlineData("https://www.kan.org.il/content/kan/kan-actual/p-11544/", true)]
    [InlineData("https://www.kan.org.il/content/kan/kan-archive/p-1/", false)]
    [InlineData("https://www.kan.org.il/content/kids/hinuchit-main/p-2/", false)]
    [InlineData("https://www.kan.org.il/content/kan/kan-11-extra/p-3/", false)]
    [InlineData("not a url", false)]
    public void OnlyKan11AndItsCurrentAffairsAreInScope(string cardHref, bool expected) =>
        Assert.Equal(expected, KanUrls.IsInScope(cardHref));

    [Fact]
    public void AnImagePathIsMadeAbsoluteWithoutItsSize() =>
        Assert.Equal(
            "https://mobapi.kan.org.il/media/h5ih4k4g/1920x1080_.jpg",
            KanUrls.Absolute("/media/h5ih4k4g/1920x1080_.jpg?width=288&height=162&v=1dc0d18ff452510"));
}
