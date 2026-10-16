using System.Reflection;
using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Hls;
using Jellyfin.Plugin.IlVod.Providers.Kan;
using Jellyfin.Plugin.IlVod.Providers.Kan.Transform;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Kan;

/// <summary>
/// <see cref="KanTransformer"/> on its own: Kan's records in, the shared model out, with no HTTP,
/// cache or clock anywhere. The records are built by hand from מה שתגידו (program 11510, seasons
/// 1–6 plus extras) and מהדורת כאן חדשות (11544, whose episodes state no season at all).
/// </summary>
public class KanTransformerTests
{
    private const string Media = "https://mobapi.kan.org.il/media/";
    private const string WhatYouSay = "מה שתגידו";
    private const string WhatYouSayId = "11510";
    private const string MasterUrl = "https://kan11.media.kan.org.il/vod/1107310/master.m3u8";

    private static readonly KanTransformer Transformer = new();

    private static readonly KanEntry SeasonSixEpisodeTwo = new(
        "1107310", "פרק 2 - 15.10.26", 6, 2, new DateTime(2026, 10, 15), new TimeSpan(0, 29, 35), Media + "ep2.jpg?width=480");

    private static readonly KanEntry SeasonSixEpisodeOne = new(
        "1107309", "פרק 1 - 08.10.26", 6, 1, new DateTime(2026, 10, 8), new TimeSpan(0, 28, 50), Media + "ep1.jpg?width=480");

    private static readonly KanEntry Trailer = new(
        "1109548", "טריילר", null, 1000, new DateTime(2026, 9, 30), new TimeSpan(0, 0, 27), null);

    [Fact]
    public void HoldsNoStateSoItCanOnlyConvert()
    {
        FieldInfo[] fields = typeof(KanTransformer).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.Empty(fields);
    }

    [Fact]
    public void ShowsKeepTheIndexOrderTheirIdsOverviewAndGenresAsKanSpellsThem()
    {
        KanShowIndex index = new(
        [
            new KanShowCard(WhatYouSayId, WhatYouSay, Media + "card.jpg", null, "תוכנית סאטירה שבועית", ["קומדיה וסאטירה"]),
            new KanShowCard("11544", "מהדורת כאן חדשות", null, null, null, ["תוכניות אקטואליה", "דוקו"]),
        ]);

        IReadOnlyList<Show> shows = Transformer.ToShows(index);

        Assert.Equal([WhatYouSayId, "11544"], shows.Select(show => show.Id));
        Assert.Equal((WhatYouSay, "תוכנית סאטירה שבועית"), (shows[0].Name, shows[0].Overview));
        Assert.Equal(["תוכניות אקטואליה", "דוקו"], shows[1].Genres);
        Assert.Null(shows[1].Overview);
    }

    [Fact]
    public void AShowsPosterPrefersTheLobbysSizedPosterThenTheCardAndElseNone()
    {
        KanShowIndex index = new(
        [
            new KanShowCard("1", "עם פוסטר", Media + "card1.jpg", Media + "poster1.jpg", null, []),
            new KanShowCard("2", "בלי פוסטר", Media + "card2.jpg", null, null, []),
            new KanShowCard("3", "בלי תמונה", null, null, null, []),
        ]);

        IReadOnlyList<Show> shows = Transformer.ToShows(index);

        Assert.Equal(
            [Media + "poster1.jpg?width=400&height=600", Media + "card2.jpg?width=640&height=360", null],
            shows.Select(show => show.PosterUrl));
    }

    [Fact]
    public void NumberedSeasonsListAfterTheExtrasInOrder()
    {
        KanEntry seasonFiveFinale = new("1100001", "פרק 12", 5, 12, null, null, null);
        KanShowPage page = new(WhatYouSayId, WhatYouSay, [SeasonSixEpisodeTwo, Trailer, SeasonSixEpisodeOne, seasonFiveFinale]);

        IReadOnlyList<Season> seasons = Transformer.ToSeasons(page);

        Assert.Equal(
            [new Season("0", 0, "פרקים נוספים"), new Season("5", 5, "עונה 5"), new Season("6", 6, "עונה 6")],
            seasons);
    }

    [Fact]
    public void AShowWhoseEpisodesStateNoSeasonHasThemAsItsOnlySeason()
    {
        KanShowPage news = NewsPage();

        Season season = Assert.Single(Transformer.ToSeasons(news));

        Assert.Equal(new Season("0", 1, "פרקים"), season);
    }

    [Fact]
    public void AShowWithNoEpisodesHasNoSeasons()
    {
        Assert.Empty(Transformer.ToSeasons(new KanShowPage(WhatYouSayId, WhatYouSay, [])));
    }

    [Fact]
    public void EpisodesAreIdentifiedByProgramAndEntryAndCarryTheFeedsDetails()
    {
        KanSeasonPage page = new(WhatYouSayId, WhatYouSay, 6, ShowHasNumberedSeasons: true, [SeasonSixEpisodeTwo, SeasonSixEpisodeOne]);

        IReadOnlyList<Episode> episodes = Transformer.ToEpisodes(page);

        Assert.Equal(
            [
                new Episode("11510-1107309", WhatYouSay, 6, 1, "פרק 1 - 08.10.26", new DateTime(2026, 10, 8), new TimeSpan(0, 28, 50), Media + "ep1.jpg?width=480"),
                new Episode("11510-1107310", WhatYouSay, 6, 2, "פרק 2 - 15.10.26", new DateTime(2026, 10, 15), new TimeSpan(0, 29, 35), Media + "ep2.jpg?width=480"),
            ],
            episodes);
    }

    [Fact]
    public void ExtrasAreFiledUnderTheSeasonNumberToSeasonsGivesThem()
    {
        KanShowPage show = new(WhatYouSayId, WhatYouSay, [SeasonSixEpisodeTwo, Trailer]);
        KanSeasonPage extras = new(WhatYouSayId, WhatYouSay, null, show.HasNumberedSeasons(), [Trailer]);

        Episode trailer = Assert.Single(Transformer.ToEpisodes(extras));

        Assert.Equal(Transformer.ToSeasons(show)[0].Number, trailer.SeasonNumber);
        Assert.Equal((0, 1000), (trailer.SeasonNumber, trailer.Number));
    }

    [Fact]
    public void AnOnlySeasonsEpisodesAreFiledUnderTheSeasonNumberToSeasonsGivesThem()
    {
        KanShowPage news = NewsPage();
        KanSeasonPage onlySeason = new(news.ProgramId, news.ShowName, null, news.HasNumberedSeasons(), news.Episodes);

        IReadOnlyList<Episode> episodes = Transformer.ToEpisodes(onlySeason);

        Assert.All(episodes, episode => Assert.Equal(Transformer.ToSeasons(news)[0].Number, episode.SeasonNumber));
        Assert.Equal([947, 948, 949], episodes.Select(episode => episode.Number));
    }

    [Fact]
    public void EpisodesWithNoNumberAreNumberedByTheirPositionFromTheOldest()
    {
        KanSeasonPage page = new(WhatYouSayId, WhatYouSay, 3, ShowHasNumberedSeasons: true,
        [
            new KanEntry("3", "החדש", 3, null, null, null, null),
            new KanEntry("2", "האמצעי", 3, null, null, null, null),
            new KanEntry("1", "הישן", 3, null, null, null, null),
        ]);

        IReadOnlyList<Episode> episodes = Transformer.ToEpisodes(page);

        Assert.Equal([(1, "הישן"), (2, "האמצעי"), (3, "החדש")], episodes.Select(episode => (episode.Number, episode.Name)));
    }

    [Fact]
    public void EpisodesSharingANumberAreOrderedByAirDateThenName()
    {
        KanSeasonPage page = new(WhatYouSayId, WhatYouSay, null, ShowHasNumberedSeasons: true,
        [
            new KanEntry("4", "ב", null, 1000, new DateTime(2026, 10, 1), null, null),
            new KanEntry("3", "א", null, 1000, new DateTime(2026, 10, 1), null, null),
            new KanEntry("2", "ג", null, 1000, new DateTime(2026, 9, 1), null, null),
        ]);

        IReadOnlyList<Episode> episodes = Transformer.ToEpisodes(page);

        Assert.Equal(["ג", "א", "ב"], episodes.Select(episode => episode.Name));
    }

    [Fact]
    public void AStreamLinkIsTheMasterPlaylistDescribedByItsBestVariantAndKansFormat()
    {
        KanStream stream = new(MasterUrl, new HlsVariant(1920, 1080, 5_000_000, "High", 4.0), new HlsStreamIndices(Video: 5, Audio: 0), new TimeSpan(0, 29, 35));

        StreamLink link = Transformer.ToStreamLink(stream);

        Assert.Equal((MasterUrl, "hls"), (link.Url, link.Container));
        Assert.Equal((1920, 1080, 5_000_000, "High", 4.0), (link.Description.Width, link.Description.Height, link.Description.BitsPerSecond, link.Description.VideoProfile, link.Description.VideoLevel));
        Assert.Same(KanStreams.Format, link.Description.Format);
        Assert.Equal(new TimeSpan(0, 29, 35), link.Description.Duration);
        Assert.Equal((5, 0), (link.Description.VideoStreamIndex, link.Description.AudioStreamIndex));
    }

    /// <summary>
    /// Kan's master lists its audio first, so the default mapping (video 0, audio 1) would hand
    /// ffmpeg the audio as the video. When the tracks' order is unknown — the master could not be
    /// read, or lists them some other way — no streams are claimed and Jellyfin probes the master.
    /// </summary>
    [Fact]
    public void AStreamWhoseTracksAreNotKnownClaimsNoFormatSoJellyfinProbesIt()
    {
        KanStream stream = new(MasterUrl, Variant: null, StreamIndices: null, Duration: new TimeSpan(0, 29, 35));

        StreamLink link = Transformer.ToStreamLink(stream);

        Assert.Equal(MasterUrl, link.Url);
        Assert.Null(link.Description.Format);
        Assert.Equal((null, null), (link.Description.Width, link.Description.Height));
        Assert.Equal(new TimeSpan(0, 29, 35), link.Description.Duration);
    }

    [Fact]
    public void AReadableMasterInAnotherLayoutClaimsNoFormatEither()
    {
        KanStream stream = new(MasterUrl, new HlsVariant(1920, 1080, 5_000_000, "High", 4.0), StreamIndices: null, Duration: null);

        Assert.Null(Transformer.ToStreamLink(stream).Description.Format);
    }

    /// <summary>מהדורת כאן חדשות's feed, newest first: no episode states a season.</summary>
    private static KanShowPage NewsPage() => new("11544", "מהדורת כאן חדשות",
    [
        new KanEntry("1109601", "מהדורת כאן חדשות 16.10.26", null, 949, new DateTime(2026, 10, 16), new TimeSpan(0, 58, 12), null),
        new KanEntry("1109502", "מהדורת כאן חדשות 15.10.26", null, 948, new DateTime(2026, 10, 15), new TimeSpan(0, 57, 40), null),
        new KanEntry("1109403", "מהדורת כאן חדשות 14.10.26", null, 947, new DateTime(2026, 10, 14), new TimeSpan(0, 59, 3), null),
    ]);
}
