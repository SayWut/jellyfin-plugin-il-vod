using System.Text.Json;
using Jellyfin.Plugin.IlVod.Providers.Reshet13;
using Jellyfin.Plugin.IlVod.Providers.Reshet13.Source;
using static Jellyfin.Plugin.IlVod.Tests.Providers.Reshet13.Reshet13Fixtures;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Reshet13;

public class KalturaAssetsTests
{
    [Fact]
    public void TheIndexKeysEverySeriesByItsSeriesIdMetaInKalturasOrder()
    {
        Reshet13ShowIndex index = KalturaAssets.ParseIndex(ObjectsOf(SeriesJson));

        Assert.Equal(["57", "518", "522", "569", "685", "425"], index.Series.Select(series => series.SeriesId));
        Assert.Equal("האח הגדול", index.Series[0].Name);
    }

    [Fact]
    public void TheOverviewPrefersTheLongSummaryThenTheDescriptionThenNothing()
    {
        Reshet13ShowIndex index = KalturaAssets.ParseIndex(ObjectsOf(SeriesJson));

        Assert.StartsWith("ליהיא גרינר חוגגת במסיבת בריכה", Series(index, "518").Overview, StringComparison.Ordinal);
        Assert.Null(Series(index, "522").Overview);
    }

    [Fact]
    public void TheLongSummaryWinsOverTheDescription()
    {
        string asset = """{"name":"x","description":"קצר","metas":{"SeriesID":{"value":"9"},"LongSummary":{"value":"ארוך"}}}""";

        Reshet13ShowIndex index = KalturaAssets.ParseIndex([Element(asset)]);

        Assert.Equal("ארוך", Assert.Single(index.Series).Overview);
    }

    [Fact]
    public void TheOverviewFallsBackToTheDescriptionWhenThereIsNoLongSummary()
    {
        string asset = """{"name":"x","description":"תיאור","metas":{"SeriesID":{"value":"9"}}}""";

        Reshet13ShowIndex index = KalturaAssets.ParseIndex([Element(asset)]);

        Assert.Equal("תיאור", Assert.Single(index.Series).Overview);
    }

    [Fact]
    public void GenresAndImagesAreReadAsKalturaListsThem()
    {
        Reshet13Series series = Series(KalturaAssets.ParseIndex(ObjectsOf(SeriesJson)), "518");

        Assert.Equal(["בידור", "תכניות אירוח", "דיגיטל 13"], series.Genres);
        Assert.Equal(["16x9", "9x16"], series.Images.Select(image => image.Ratio));
        Assert.All(series.Images, image => Assert.StartsWith("https://images.frp1.ott.kaltura.com/", image.Url, StringComparison.Ordinal));
    }

    [Fact]
    public void ASeriesWithoutASeriesIdIsSkipped()
    {
        Reshet13ShowIndex index = KalturaAssets.ParseIndex([Element("""{"name":"orphan"}"""), Element("42")]);

        Assert.Empty(index.Series);
    }

    [Fact]
    public void AnEpisodeCarriesItsIndicesTagsDurationAndEntryId()
    {
        Reshet13Episode episode = EpisodesOf57().Single(candidate => candidate.AssetId == KnownAssetId);

        Assert.Equal("האח הגדול, עונה 8, פרק 65: הגמר הגדול", episode.Name);
        Assert.Equal(1, episode.SeasonIndex);
        Assert.Equal(2, episode.EpisodeIndex);
        Assert.Equal("עונה 8", episode.SeasonName);
        Assert.Equal("האח הגדול", episode.SeriesName);
        Assert.Equal(TimeSpan.FromSeconds(5552), episode.Duration);
        Assert.Equal(KnownEntryId, episode.EntryId);
        Assert.Equal("16x9", Assert.Single(episode.Images).Ratio);
    }

    [Fact]
    public void AnEpisodeWithNoIndicesAndAZeroDurationStatesNeither()
    {
        Reshet13Episode episode = EpisodesOf57().Single(candidate => candidate.AssetId == "2207940");

        Assert.Null(episode.SeasonIndex);
        Assert.Null(episode.EpisodeIndex);
        Assert.Null(episode.SeasonName);
        Assert.Null(episode.Duration);
        Assert.Equal("1_cizqgast", episode.EntryId);
    }

    [Fact]
    public void EveryEpisodeOfTheFixtureIsReadInKalturasOrder()
    {
        IReadOnlyList<Reshet13Episode> episodes = EpisodesOf57();

        Assert.Equal(9, episodes.Count);
        Assert.Equal([null, 1, 1, 4, 4, 6, 10, 3, 3], episodes.Select(episode => episode.SeasonIndex));
    }

    [Fact]
    public void AnEpisodeWithNoHlsFileHasNoEntryId()
    {
        string asset = """{"id":5,"name":"x","mediaFiles":[{"type":"dash_widevine","duration":10,"url":"https://h/entryId/1_dash/format/mpegdash/a.mpd"}]}""";

        Reshet13Episode episode = KalturaAssets.ParseEpisode(Element(asset));

        Assert.Null(episode.EntryId);
        Assert.Null(episode.Duration);
    }

    [Fact]
    public void EpisodesOfAnotherSeriesAreDropped()
    {
        string stray = """{"id":7,"name":"stray","metas":{"SeriesID":{"value":"99"}}}""";

        IReadOnlyList<Reshet13Episode> episodes = KalturaAssets.ParseEpisodes([.. ObjectsOf(EpisodesJson), Element(stray)], SeriesId);

        Assert.DoesNotContain(episodes, episode => episode.AssetId == "7");
        Assert.Equal(9, episodes.Count);
    }

    [Fact]
    public void ASingleAssetIsReadAsAnEpisode()
    {
        Reshet13Episode episode = KalturaAssets.ParseEpisode(ResultOf(AssetGetJson));

        Assert.Equal(AssetGetId, episode.AssetId);
        Assert.Equal(AssetGetEntryId, episode.EntryId);
        Assert.Equal(TimeSpan.FromSeconds(3822), episode.Duration);
    }

    [Fact]
    public void AnAnswerThatIsNoAssetIsAFormatException()
    {
        Assert.Throws<FormatException>(() => KalturaAssets.ParseEpisode(Element("""{"name":"no id"}""")));
        Assert.Throws<FormatException>(() => KalturaAssets.ParseEpisode(Element("[]")));
    }

    [Fact]
    public void SeasonAssetNamesAreKeyedBySeasonIndex()
    {
        IReadOnlyDictionary<int, string> names = KalturaAssets.ParseSeasonNames(ObjectsOf(SeasonsJson));

        Assert.Equal(7, names.Count);
        Assert.Equal("עונה 5", names[4]);
        Assert.Equal("העונה החמה • קיץ 2021", names[6]);
        Assert.Equal("עונה 1", names[10]);
    }

    private static IReadOnlyList<Reshet13Episode> EpisodesOf57() => KalturaAssets.ParseEpisodes(ObjectsOf(EpisodesJson), SeriesId);

    private static Reshet13Series Series(Reshet13ShowIndex index, string seriesId) =>
        index.Series.Single(series => series.SeriesId == seriesId);

    private static JsonElement ResultOf(string answer) => Element(answer).GetProperty("result");

    private static JsonElement[] ObjectsOf(string answer) => [.. ResultOf(answer).GetProperty("objects").EnumerateArray()];

    private static JsonElement Element(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
