using Jellyfin.Plugin.IlVod.MakoApi;
using Jellyfin.Plugin.IlVod.Tests.Support;

namespace Jellyfin.Plugin.IlVod.Tests.Mako;

public class MakoIndexTests
{
    private static readonly string MenuJson = FixtureFile.ReadText("genre-menu.json");
    private static readonly string ComedyJson = FixtureFile.ReadText("genre-list-comedy.json");

    [Fact]
    public void OnlyTheRealGenresAreKeptFromTheMenu()
    {
        IReadOnlyList<Genre> genres = MakoIndex.ParseGenres(MenuJson);

        Assert.Equal(new[] { "ריאליטי", "דוקומנטרי", "דרמה", "קומדיה", "בישול" }, genres.Select(genre => genre.Name));
        Assert.All(genres, genre => Assert.StartsWith("/mako-vod-index?filter=genre&vcmId=", genre.PagePath, StringComparison.Ordinal));
    }

    [Fact]
    public void ProvidersTheAbcListAndPlaylistsAreNotGenres()
    {
        IReadOnlyList<Genre> genres = MakoIndex.ParseGenres(MenuJson);

        Assert.DoesNotContain(genres, genre => genre.Name is "החדשות" or "פודקאסטים" or "ערוץ 24" or "פלייליסטים" or "כל התכניות");
        Assert.DoesNotContain(genres, genre => genre.PagePath.Contains("ABC_Programs_Slider", StringComparison.Ordinal));
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
