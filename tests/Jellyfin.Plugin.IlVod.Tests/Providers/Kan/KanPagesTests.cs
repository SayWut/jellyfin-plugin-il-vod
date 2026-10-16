using Jellyfin.Plugin.IlVod.Providers.Kan.Source;
using Jellyfin.Plugin.IlVod.Tests.Support;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Kan;

public class KanPagesTests
{
    private const string DramaSlug = "%D7%93%D7%A8%D7%9E%D7%94";

    private static readonly KanLobby Lobby = KanPages.ParseLobby(FixtureFile.ReadText("kan-lobby.html"));
    private static readonly KanGenrePage DramaPage1 = KanPages.ParseGenrePage(FixtureFile.ReadText("kan-genre-drama-1.html"));
    private static readonly KanGenrePage DramaPage2 = KanPages.ParseGenrePage(FixtureFile.ReadText("kan-genre-drama-2.html"));

    [Fact]
    public void TheLobbyListsEveryRailInOrder()
    {
        Assert.Equal(24, Lobby.Rails.Count);
        Assert.Equal(new KanRail("דרמה", DramaSlug), Lobby.Rails[0]);
        Assert.Equal("הליגה הלאומית", Lobby.Rails[^1].Name);
    }

    [Fact]
    public void ARailsNameIsDecoded() =>
        Assert.Contains(Lobby.Rails, rail => rail.Name == "לא מכאן: סדרות חו\"ל");

    [Fact]
    public void TheLobbyGivesAProgramsPosterWithoutItsSizeAndItsSynopsis()
    {
        KanLobbyCard card = Lobby.CardsByProgramId["1033206"];

        Assert.Equal("https://mobapi.kan.org.il/media/fd1h5wxu/poster-image_small_239x360_%D7%9C%D7%90-%D7%9C%D7%A8%D7%99%D7%91.jpg", card.PosterUrl);
        Assert.Equal("שלושה אחים מנסים להחזיר לעצמם את הבית שאביהם הוריש לנערת ליווי | 14+", card.Overview);
    }

    [Fact]
    public void ALobbyCardWithNoSynopsisHasNoOverview() =>
        Assert.Null(Lobby.CardsByProgramId["671003"].Overview);

    [Fact]
    public void ALobbyCardThatLinksToNoProgramIsLeftOut() =>
        Assert.Equal(47, Lobby.CardsByProgramId.Count);

    [Theory]
    [InlineData("<html><body>Just a moment...</body></html>")]
    [InlineData("")]
    public void APageWithNoRailsIsNoLobby(string html) =>
        Assert.Throws<FormatException>(() => KanPages.ParseLobby(html));

    [Fact]
    public void AGenrePageListsItsCardsItsStatedTotalAndItsLastPage()
    {
        Assert.Equal(30, DramaPage1.Cards.Count);
        Assert.Equal(40, DramaPage1.StatedTotal);
        Assert.Equal(2, DramaPage1.LastPage);
        Assert.Equal(10, DramaPage2.Cards.Count);
        Assert.Equal(2, DramaPage2.LastPage);
    }

    [Fact]
    public void AGenreCardIsItsProgramsIdNameLinkAndImageWithoutItsSize()
    {
        Assert.Equal(
            new KanGenreCard(
                "13291",
                "שטוקהולם",
                "https://www.kan.org.il/content/kan/kan-11/p-13291/",
                "https://mobapi.kan.org.il/media/h5ih4k4g/1920x1080_.jpg"),
            DramaPage1.Cards[0]);
    }

    [Fact]
    public void AGenreCardsNameIsDecoded() =>
        Assert.Contains(DramaPage1.Cards, card => card.Name == "פמת\"א");

    [Fact]
    public void AGenrePageWithNoPagingIsItsOwnLastPage()
    {
        KanGenrePage page = KanPages.ParseGenrePage(KanHtml.GenrePage(statedTotal: 1, lastPage: null, KanHtml.Card("7")));

        Assert.Equal(1, page.LastPage);
        Assert.Equal("7", Assert.Single(page.Cards).ProgramId);
    }

    [Fact]
    public void AnEmptyGenreStatesNoResults()
    {
        KanGenrePage page = KanPages.ParseGenrePage(KanHtml.GenrePage(statedTotal: 0, lastPage: null));

        Assert.Empty(page.Cards);
        Assert.Equal(0, page.StatedTotal);
    }

    /// <summary>The stated total is what a walk is checked against: without it a pager that broke
    /// would end the walk at page 1 and the catalogue would come back short, unnoticed.</summary>
    [Fact]
    public void APageWithCardsButNoStatedTotalIsNoGenrePage() =>
        Assert.Throws<FormatException>(() => KanPages.ParseGenrePage(KanHtml.GenrePage(statedTotal: null, lastPage: null, KanHtml.Card("7"))));

    [Fact]
    public void ACardWithNoImageHasNone() =>
        Assert.Null(Assert.Single(KanPages.ParseGenrePage(KanHtml.GenrePage(1, null, KanHtml.Card("7", image: null))).Cards).ImageUrl);

    [Theory]
    [InlineData("<html><body>Just a moment...</body></html>")]
    [InlineData("""<li class="grid-list-item"><div class="card">no link</div></li> 1 תוצאות""")]
    public void APageWithNoTotalOrWithABrokenCardIsNoGenrePage(string html) =>
        Assert.Throws<FormatException>(() => KanPages.ParseGenrePage(html));
}
