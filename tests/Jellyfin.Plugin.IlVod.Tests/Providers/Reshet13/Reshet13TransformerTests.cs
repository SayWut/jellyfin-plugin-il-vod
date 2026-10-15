using System.Reflection;
using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Hls;
using Jellyfin.Plugin.IlVod.Providers.Reshet13;
using Jellyfin.Plugin.IlVod.Providers.Reshet13.Transform;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Reshet13;

/// <summary>
/// <see cref="Reshet13Transformer"/> on its own: Reshet 13's records in, the shared model out, with
/// no HTTP, cache or clock anywhere. The records are built by hand from האח הגדול's fixtures
/// (<c>reshet13-episodes-57.json</c>, <c>reshet13-seasons-57.json</c>), whose season indices run
/// backwards (index 1 is "עונה 8", index 10 is "עונה 1").
/// </summary>
public class Reshet13TransformerTests
{
    private const string ImageBase = "https://images.frp1.ott.kaltura.com/Service.svc/GetImage/p/5031/entry_id/";
    private const string BigBrother = "האח הגדול";

    private static readonly Reshet13Transformer Transformer = new();

    // index → name, as reshet13-seasons-57.json gives them.
    private static readonly Dictionary<int, string> BigBrotherSeasonAssets = new()
    {
        [4] = "עונה 5",
        [5] = "עונה 4",
        [6] = "העונה החמה • קיץ 2021",
        [7] = "עונה 3",
        [8] = "עונה 2",
        [9] = "VIP עונה 1",
        [10] = "עונה 1",
    };

    [Fact]
    public void HoldsNoStateSoItCanOnlyConvert()
    {
        FieldInfo[] fields = typeof(Reshet13Transformer).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.Empty(fields);
    }

    [Fact]
    public void ShowsKeepTheIndexOrderAndTheirGenresAsReshet13SpellsThem()
    {
        Reshet13ShowIndex index = new(
        [
            new Reshet13Series("57", BigBrother, "הבית הכי מפורסם במדינה!", [new KalturaImage(ImageBase + "62077ca37c744da78585700571a6f6dd/version/0", "2x3")], ["ריאליטי"]),
            new Reshet13Series("518", "מסיבת בריכה", Overview: null, [new KalturaImage(ImageBase + "6c442517faef4da7906f8e60844e5166/version/11", "9x16")], ["בידור", "תכניות אירוח", "דיגיטל 13"]),
        ]);

        IReadOnlyList<Show> shows = Transformer.ToShows(index);

        Assert.Equal(["57", "518"], shows.Select(show => show.Id));
        Assert.Equal((BigBrother, "הבית הכי מפורסם במדינה!"), (shows[0].Name, shows[0].Overview));
        Assert.Equal(["בידור", "תכניות אירוח", "דיגיטל 13"], shows[1].Genres);
        Assert.Null(shows[1].Overview);
    }

    [Fact]
    public void AShowsPosterIsItsPreferredImageAndAShowWithNoneHasNoPoster()
    {
        Reshet13ShowIndex index = new(
        [
            new Reshet13Series("518", "מסיבת בריכה", null, [new KalturaImage(ImageBase + "aa051277231f452cb036b71c94ab88d3/version/11", "16x9"), new KalturaImage(ImageBase + "6c442517faef4da7906f8e60844e5166/version/11", "9x16")], []),
            new Reshet13Series("1", "בלי תמונה", null, [], []),
        ]);

        IReadOnlyList<Show> shows = Transformer.ToShows(index);

        Assert.Equal(ImageBase + "6c442517faef4da7906f8e60844e5166/version/11/width/405/height/720/quality/80", shows[0].PosterUrl);
        Assert.Null(shows[1].PosterUrl);
    }

    [Fact]
    public void ASeasonIsNamedByItsSeasonAssetFirst()
    {
        Reshet13ShowPage page = new(BigBrotherSeasonAssets, [Episode("2361602", "האח הגדול, עונה 5, פרק 59: חוזרים הביתה", 4, 1, seasonName: "עונה 5")]);

        Season season = Assert.Single(Transformer.ToSeasons(page));

        Assert.Equal(new Season("4", 5, "עונה 5"), season);
    }

    [Fact]
    public void ASeasonWithNoSeasonAssetIsNamedByItsEpisodesSeasonNameTag()
    {
        Reshet13ShowPage page = new(BigBrotherSeasonAssets, [Episode("4471744", "האח הגדול, עונה 8, פרק 65: הגמר הגדול", 1, 2, seasonName: "עונה 8")]);

        Season season = Assert.Single(Transformer.ToSeasons(page));

        Assert.Equal(new Season("1", 8, "עונה 8"), season);
    }

    [Fact]
    public void ASeasonWhoseEpisodesTagTwoSeasonsIsNamedByTheCommonerOneAndOnATieTheFirstSeen()
    {
        Reshet13ShowPage page = new(BigBrotherSeasonAssets,
        [
            Episode("3043844", "האח הגדול, עונה 6, פרק 1: אירוע הכניסה החלק ראשון", 3, 67, seasonName: "עונה 6"),
            Episode("2164350", "האח הגדול, עונה 2, פרק 7: פרשת גואל רצון מתפוצצת בבית", 3, 8, seasonName: "עונה 2"),
        ]);

        Season season = Assert.Single(Transformer.ToSeasons(page));

        Assert.Equal(new Season("3", 6, "עונה 6"), season);
    }

    [Fact]
    public void ASeasonTaggedMostlyAsOneSeasonIsNamedByThatOne()
    {
        Reshet13ShowPage page = new(BigBrotherSeasonAssets,
        [
            Episode("3043844", "פרק 1", 3, 67, seasonName: "עונה 6"),
            Episode("2164350", "פרק 7", 3, 8, seasonName: "עונה 2"),
            Episode("2164351", "פרק 8", 3, 9, seasonName: "עונה 2"),
        ]);

        Assert.Equal("עונה 2", Assert.Single(Transformer.ToSeasons(page)).Name);
    }

    [Fact]
    public void ASeasonWithNoAssetAndNoTagIsNamedAfterItsIndex()
    {
        Reshet13ShowPage page = new(new Dictionary<int, string>(), [Episode("100", "פרק 1", 2, 1, seasonName: null)]);

        Season season = Assert.Single(Transformer.ToSeasons(page));

        Assert.Equal(new Season("2", 2, "עונה 2"), season);
    }

    [Fact]
    public void EpisodesWithNoSeasonIndexShareOneExtrasSeasonNumberedZero()
    {
        Reshet13ShowPage page = new(BigBrotherSeasonAssets, [Episode("2207940", "פולואח, פרק 2, מה קורה עם החשבון של גיא?", null, null, seasonName: null)]);

        Season season = Assert.Single(Transformer.ToSeasons(page));

        Assert.Equal(new Season(Reshet13SeasonId.NoSeason, 0, "פרקים נוספים"), season);
    }

    [Theory]
    [InlineData(9, "VIP עונה 1", 1)]
    [InlineData(10, "עונה 1", 1)]
    [InlineData(6, "העונה החמה • קיץ 2021", 6)]
    public void ASeasonIsNumberedByTheNumberAfterUnaInItsNameElseByItsIndex(int seasonIndex, string expectedName, int expectedNumber)
    {
        Reshet13ShowPage page = new(BigBrotherSeasonAssets, [Episode("1", "פרק 1", seasonIndex, 1, seasonName: null)]);

        Season season = Assert.Single(Transformer.ToSeasons(page));

        Assert.Equal((expectedName, expectedNumber), (season.Name, season.Number));
    }

    [Fact]
    public void ThereIsOneSeasonPerIndexOrderedByNumberThenName()
    {
        Reshet13ShowPage page = new(BigBrotherSeasonAssets,
        [
            Episode("2207940", "פולואח, פרק 2", null, null, seasonName: null),
            Episode("4480642", "האח הגדול, עונה 8: חוזרים הביתה", 1, 1, seasonName: "עונה 8"),
            Episode("4471744", "האח הגדול, עונה 8, פרק 65: הגמר הגדול", 1, 2, seasonName: "עונה 8"),
            Episode("2361602", "האח הגדול, עונה 5, פרק 59: חוזרים הביתה", 4, 1, seasonName: "עונה 5"),
            Episode("2027074", "האח הגדול העונה החמה, פרק 26: ימים אחרונים בבית", 6, 25, seasonName: "העונה החמה • קיץ 2021"),
            Episode("3177483", "האח הגדול - הבית נחשף: הסרט מלא", 10, 53, seasonName: "עונה 1"),
            Episode("9000001", "פרק 1", 9, 1, seasonName: null),
        ]);

        IReadOnlyList<Season> seasons = Transformer.ToSeasons(page);

        Assert.Equal(
            [(0, "פרקים נוספים"), (1, "VIP עונה 1"), (1, "עונה 1"), (5, "עונה 5"), (6, "העונה החמה • קיץ 2021"), (8, "עונה 8")],
            seasons.Select(season => (season.Number, season.Name)));
        Assert.Equal(["none", "9", "10", "4", "6", "1"], seasons.Select(season => season.Id));
    }

    [Fact]
    public void AnEpisodeIsTheAssetWithTheSeasonPagesShowNameAndItsWideImage()
    {
        Reshet13SeasonPage page = new(BigBrother, 1, SeasonAssetName: null,
        [
            new Reshet13Episode("4471744", "האח הגדול, עונה 8, פרק 65: הגמר הגדול", 1, 2, "עונה 8", BigBrother, TimeSpan.FromSeconds(5552), [new KalturaImage(ImageBase + "cad198d5ad00401dbf5e43ad7a00fa6b/version/8", "16x9")], "1_f9hhlezq"),
        ]);

        Episode episode = Assert.Single(Transformer.ToEpisodes(page));

        Assert.Equal(
            new Episode("4471744", BigBrother, 8, 65, "האח הגדול, עונה 8, פרק 65: הגמר הגדול", AirDate: null, TimeSpan.FromSeconds(5552), ImageBase + "cad198d5ad00401dbf5e43ad7a00fa6b/version/8/width/640/height/360/quality/80"),
            episode);
    }

    [Fact]
    public void AnEpisodeIsNumberedByPerekInItsNameElseItsIndexElseItsPositionAndOrderedByNumber()
    {
        Reshet13SeasonPage page = new(BigBrother, SeasonIndex: null, SeasonAssetName: null,
        [
            Episode("1", "ללא מספר", null, null, seasonName: null),
            Episode("2207940", "פולואח, פרק 2, מה קורה עם החשבון של גיא?", null, null, seasonName: null),
            Episode("3", "עם אינדקס", null, 7, seasonName: null),
        ]);

        IReadOnlyList<Episode> episodes = Transformer.ToEpisodes(page);

        Assert.Equal([(1, "1"), (2, "2207940"), (7, "3")], episodes.Select(episode => (episode.Number, episode.Id)));
    }

    [Fact]
    public void EpisodesWithTheSameNumberAreOrderedByName()
    {
        Reshet13SeasonPage page = new(BigBrother, 1, "עונה 8",
        [
            Episode("2", "ב, פרק 1", 1, 5, seasonName: null),
            Episode("1", "א, פרק 1", 1, 6, seasonName: null),
        ]);

        Assert.Equal(["1", "2"], Transformer.ToEpisodes(page).Select(episode => episode.Id));
    }

    [Theory]
    [InlineData(1, null, 8)]
    [InlineData(4, "עונה 5", 5)]
    [InlineData(null, null, 0)]
    public void AnEpisodesSeasonNumberIsTheNumberItsSeasonIsListedUnder(int? seasonIndex, string? seasonAssetName, int expected)
    {
        Reshet13Episode[] episodes = [Episode("4471744", "האח הגדול, עונה 8, פרק 65: הגמר הגדול", seasonIndex, 2, seasonName: seasonIndex == 1 ? "עונה 8" : null)];
        Dictionary<int, string> assets = seasonIndex is { } index && seasonAssetName is not null ? new() { [index] = seasonAssetName } : [];

        Season season = Assert.Single(Transformer.ToSeasons(new Reshet13ShowPage(assets, episodes)));
        Episode episode = Assert.Single(Transformer.ToEpisodes(new Reshet13SeasonPage(BigBrother, seasonIndex, seasonAssetName, episodes)));

        Assert.Equal(expected, season.Number);
        Assert.Equal(season.Number, episode.SeasonNumber);
    }

    [Fact]
    public void AStreamWithAVariantIsDescribedFromItInReshet13sFormat()
    {
        const string manifest = "https://cdnapisec.kaltura.com/p/2748741/sp/274874100/playManifest/entryId/1_f9hhlezq/format/applehttp/protocol/https/a.m3u8";
        Reshet13Stream stream = new(manifest, new HlsVariant(1280, 720, 2_628_000, "Main", 3.1), TimeSpan.FromSeconds(5552));

        StreamLink link = Transformer.ToStreamLink(stream);

        Assert.Equal((manifest, "hls"), (link.Url, link.Container));
        Assert.Equal((1280, 720, 2_628_000, "Main", 3.1), (link.Description.Width, link.Description.Height, link.Description.BitsPerSecond, link.Description.VideoProfile, link.Description.VideoLevel));
        Assert.Equal(TimeSpan.FromSeconds(5552), link.Description.Duration);
        Assert.Equal(Reshet13Streams.Format, link.Description.Format);
    }

    [Fact]
    public void AStreamWithNoVariantIsDescribedAsUnmeasuredInReshet13sFormat()
    {
        StreamLink link = Transformer.ToStreamLink(new Reshet13Stream("https://cdn/a.m3u8", Variant: null, Duration: null));

        Assert.Equal(new StreamDescription(null, null, null, MasterPlaylist.UnmeasuredProfile, null) { Format = Reshet13Streams.Format }, link.Description);
    }

    private static Reshet13Episode Episode(string assetId, string name, int? seasonIndex, int? episodeIndex, string? seasonName) =>
        new(assetId, name, seasonIndex, episodeIndex, seasonName, BigBrother, Duration: null, Images: [], EntryId: null);
}
