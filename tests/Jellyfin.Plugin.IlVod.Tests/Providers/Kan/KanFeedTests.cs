using Jellyfin.Plugin.IlVod.Providers.Kan;
using Jellyfin.Plugin.IlVod.Providers.Kan.Source;
using Jellyfin.Plugin.IlVod.Tests.Support;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Kan;

public class KanFeedTests
{
    private static readonly KanShowPage Satire = KanFeed.Parse(FixtureFile.ReadText("kan-program-11510.json"), "11510");

    [Fact]
    public void AFeedIsItsProgramsNameAndEveryEpisodeInOrder()
    {
        Assert.Equal("11510", Satire.ProgramId);
        Assert.Equal("מה שתגידו", Satire.ShowName);
        Assert.Equal(102, Satire.Episodes.Count);
        Assert.Equal("1107310", Satire.Episodes[0].EntryId);
    }

    [Fact]
    public void AnEpisodeCarriesWhatTheFeedStates()
    {
        KanEntry episode = Satire.Episodes[0];

        Assert.Equal("פרק 2 - 15.10.26", episode.Title);
        Assert.Equal(6, episode.Season);
        Assert.Equal(2, episode.EpisodeNumber);
        Assert.Equal(new DateTime(2026, 10, 15), episode.BroadcastDate);
        Assert.Equal(new TimeSpan(0, 29, 35), episode.Duration);
        Assert.Equal(
            "https://mobapi.kan.org.il/media/0xgpuh0r/ep02-master-digital00_03_36_23still001.jpg?width=480",
            episode.ThumbnailUrl);
    }

    [Fact]
    public void TheSeasonsAreOneToSixAndSeasonZeroIsNoSeason()
    {
        Assert.Equal([1, 2, 3, 4, 5, 6], Satire.Episodes.Select(e => e.Season).OfType<int>().Distinct().Order());
        Assert.Equal(5, Satire.Episodes.Count(e => e.Season is null));
        Assert.True(Satire.HasNumberedSeasons());
    }

    [Fact]
    public void AnEpisodeWithNoStillHasNoThumbnail() =>
        Assert.Null(Single("1048950").ThumbnailUrl);

    [Fact]
    public void AnEpisodeNumberedZeroStatesNoNumberAndAnEmptyDateNoDate()
    {
        KanEntry special = Single("327781");

        Assert.Null(special.EpisodeNumber);
        Assert.Null(special.BroadcastDate);
    }

    [Fact]
    public void TitlesAreTrimmed() =>
        Assert.Equal("פרק 9 - 27.07.23", Single("463294").Title);

    [Fact]
    public void AProgramWithNoSeasonsHasOnlyUnnumberedEpisodes()
    {
        KanShowPage news = KanFeed.Parse(FixtureFile.ReadText("kan-program-11544.json"), "11544");

        Assert.Equal(5, news.Episodes.Count);
        Assert.False(news.HasNumberedSeasons());
        Assert.Equal(new TimeSpan(1, 34, 0), news.Episodes[0].Duration);
    }

    [Fact]
    public void AudioEntriesAreNeverEpisodes()
    {
        KanShowPage page = KanFeed.Parse(Feed(Entry("1", "audio"), Entry("2", "video"), Entry("3", "youtube")), "9");

        Assert.Equal(["2", "3"], page.Episodes.Select(e => e.EntryId));
    }

    [Fact]
    public void ALengthTheFeedStatesAsZeroIsTakenFromTheShortLength()
    {
        string entry = Entry("1", "video", duration: "00:00:00", durationLength: "1:00:23");

        Assert.Equal(new TimeSpan(1, 0, 23), Assert.Single(KanFeed.Parse(Feed(entry), "9").Episodes).Duration);
    }

    [Fact]
    public void ALengthStatedNowhereIsNoLength()
    {
        string entry = Entry("1", "video", duration: "", durationLength: "");

        Assert.Null(Assert.Single(KanFeed.Parse(Feed(entry), "9").Episodes).Duration);
    }

    [Fact]
    public void AnEntryWithNoNumericIdIsSkipped() =>
        Assert.Empty(KanFeed.Parse(Feed(Entry("abc", "video")), "9").Episodes);

    [Fact]
    public void AProgramWithNoEntriesIsAnEmptyShow() =>
        Assert.Empty(KanFeed.Parse("""{"title":"סרט","entry":[]}""", "9").Episodes);

    [Theory]
    [InlineData("""{"title":"","entry":[]}""")]
    [InlineData("""{"entry":[]}""")]
    [InlineData("""[]""")]
    [InlineData("<html>challenge</html>")]
    public void AnAnswerThatNamesNoProgramIsNotAFeed(string answer) =>
        Assert.Throws<FormatException>(() => KanFeed.Parse(answer, "9"));

    private static KanEntry Single(string entryId) => Satire.Episodes.Single(e => e.EntryId == entryId);

    private static string Feed(params string[] entries) => $$"""{"title":"תוכנית","entry":[{{string.Join(',', entries)}}]}""";

    private static string Entry(string id, string type, string duration = "00:10:00", string durationLength = "10:00") =>
        "{\"id\":\"" + id + "\",\"title\":\"פרק\",\"type\":{\"value\":\"" + type + "\"},"
        + "\"extensions\":{\"duration_length\":\"" + durationLength + "\","
        + "\"analyticsCustomProperties\":{\"season\":1,\"episode_number\":1,\"duration\":\"" + duration + "\"}}}";
}
