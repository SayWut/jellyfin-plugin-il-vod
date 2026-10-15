using Jellyfin.Plugin.IlVod.Providers.Mako;
using Jellyfin.Plugin.IlVod.Providers.Mako.Source;
using Jellyfin.Plugin.IlVod.Tests.Support;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Mako;

public class MakoIndexTests
{
    private static readonly string MenuJson = FixtureFile.ReadText("genre-menu.json");
    private static readonly string ComedyJson = FixtureFile.ReadText("genre-list-comedy.json");

    /// <summary>The lists whose names become show genres: the five real genres plus the one
    /// provider list allowlisted as a category (החדשות), still in the menu's own order.</summary>
    [Fact]
    public void TheCategoryListsAreTheRealGenresAndTheNewsInMenuOrder()
    {
        IReadOnlyList<Genre> genres = MakoIndex.ParseGenres(MenuJson);

        Assert.Equal(new[] { "ריאליטי", "דוקומנטרי", "דרמה", "קומדיה", "בישול", "החדשות" }, genres.Select(genre => genre.Name));
        Assert.All(genres.Take(5), genre => Assert.StartsWith("/mako-vod-index?filter=genre&vcmId=", genre.PagePath, StringComparison.Ordinal));
        Assert.Equal("/mako-vod-index?filter=provider&vcmId=ee06c13070733210VgnVCM2000002a0c10acRCRD", genres[5].PagePath);
    }

    /// <summary>Podcast and ערוץ 24 shows are not in mako's full show list at all, so their
    /// provider lists stay out; the A–B list and playlists only regroup shows listed elsewhere.</summary>
    [Fact]
    public void PodcastsChannel24TheAbcListAndPlaylistsAreNotCategoryLists()
    {
        IReadOnlyList<Genre> genres = MakoIndex.ParseGenres(MenuJson);

        Assert.DoesNotContain(genres, genre => genre.Name is "פודקאסטים" or "ערוץ 24" or "פלייליסטים" or "כל התכניות");
        Assert.DoesNotContain(genres, genre => genre.PagePath.Contains("ABC_Programs_Slider", StringComparison.Ordinal));
        Assert.DoesNotContain(genres, genre => genre.PagePath.Contains("filter=playlists", StringComparison.Ordinal));
    }

    /// <summary>The allowlist is a menu title on a provider list: the same title on any other
    /// kind of entry, or another provider list, is not a category.</summary>
    [Theory]
    [InlineData("החדשות", "/mako-vod-index?filter=provider&vcmId=ee06", true)]
    [InlineData("פודקאסטים", "/mako-vod-index?filter=provider&vcmId=d5aa", false)]
    [InlineData("ערוץ 24", "/mako-vod-index?filter=provider&vcmId=3377", false)]
    [InlineData("החדשות", "/mako-vod-index?filter=playlists", false)]
    [InlineData("החדשות", "/mako-vod-keshet/news?filter=provider", false)]
    [InlineData("דרמה", "/mako-vod-index?filter=genre&vcmId=05fc", true)]
    public void OnlyARealGenreOrAnAllowlistedProviderListIsACategoryList(string title, string pagePath, bool expected)
    {
        Assert.Equal(expected, MakoIndexPaths.IsCategoryList(title, pagePath));
    }

    [Fact]
    public void TheFullIndexYieldsEveryShowItNames()
    {
        IReadOnlyList<IndexEntry> entries = MakoIndex.ParseEntries(FixtureFile.ReadText("mako-index-all.json"));

        Assert.Equal(19, entries.Count);
        Assert.Equal("חדשות הבוקר עם ניב רסקין", Assert.Single(entries, entry => entry.PagePath == "/mako-vod-keshet/morning_news").Name);
    }

    [Fact]
    public void AGenreListYieldsOneEntryPerShowInMakosOrder()
    {
        IReadOnlyList<IndexEntry> entries = MakoIndex.ParseEntries(ComedyJson);

        Assert.Equal(5, entries.Count);
        Assert.Equal(new IndexEntry("/mako-vod-keshet/shemesh", "שמש", "https://img.mako.co.il/2025/06/09/Shemesh_e.jpg"), entries[0]);
        Assert.Equal("/mako-vod-keshet/Arabic-work", entries[1].PagePath);
    }

    [Fact]
    public void AnEntryWithoutAPathOrTitleIsSkippedAndAMissingPictureIsNull()
    {
        const string list = """{"items":[{"title":"no path"},{"pageUrl":"/x","title":""},{"pageUrl":"/mako-vod-keshet/ok","title":"ok"},"not an object"]}""";

        IReadOnlyList<IndexEntry> entries = MakoIndex.ParseEntries(list);

        Assert.Equal([new IndexEntry("/mako-vod-keshet/ok", "ok", null)], entries);
    }

    [Fact]
    public void AMenuEntryWithoutAUrlOrTitleIsSkipped()
    {
        const string menu = """{"menuItems":[{"title":"x"},{"url":"/mako-vod-index?filter=genre&vcmId=1"},{"title":"דרמה","url":"/mako-vod-index?filter=genre&vcmId=2"}]}""";

        Assert.Equal([new Genre("דרמה", "/mako-vod-index?filter=genre&vcmId=2")], MakoIndex.ParseGenres(menu));
    }

    [Theory]
    [InlineData("<html>Access Denied</html>", "mako index page is not valid JSON")]
    [InlineData("[]", "mako index page is not a JSON object")]
    public void TextThatIsNotAnIndexPageIsAFormatException(string text, string message)
    {
        Assert.Equal(message, Assert.Throws<FormatException>(() => MakoIndex.ParseGenres(text)).Message);
        Assert.Equal(message, Assert.Throws<FormatException>(() => MakoIndex.ParseEntries(text)).Message);
    }

    [Fact]
    public void APageWithoutTheArrayIsEmptyNotAFailure()
    {
        Assert.Empty(MakoIndex.ParseGenres("{}"));
        Assert.Empty(MakoIndex.ParseEntries("{\"items\":\"nope\"}"));
    }

    [Theory]
    [InlineData("/mako-vod-index?filter=genre&vcmId=4f9d", true)]
    [InlineData("/mako-vod-index?filter=provider&vcmId=ee06", false)]
    [InlineData("/mako-vod-index?filter=ABC_Programs_Slider", false)]
    [InlineData("/mako-vod-index", false)]
    [InlineData("/mako-vod-keshet/shemesh?filter=genre", false)]
    public void OnlyAnIndexPathWithTheGenreFilterIsAGenreList(string pagePath, bool expected)
    {
        Assert.Equal(expected, MakoIndexPaths.IsGenreList(pagePath));
    }
}
