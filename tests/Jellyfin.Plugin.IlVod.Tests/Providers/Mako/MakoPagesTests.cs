using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Providers.Mako;
using Jellyfin.Plugin.IlVod.Providers.Mako.Source;
using Jellyfin.Plugin.IlVod.Providers.Mako.Transform;
using Jellyfin.Plugin.IlVod.Tests.Support;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Mako;

public class MakoPagesTests
{
    private const string ShowName = "שמש";
    private static readonly string ShowPageJson = FixtureFile.ReadText("show-page-shemesh.json");
    private static readonly string SeasonOnePageJson = FixtureFile.ReadText("season-page-shemesh-s1.json");

    [Fact]
    public void ParsesEverySeasonOfTheShowPageNumberedFromItsHebrewTitle()
    {
        ShowPage page = MakoPages.ParseShowPage(ShowPageJson);

        Assert.Equal(Enumerable.Range(1, 6), page.Seasons.Select(season => season.Number));
        Assert.Equal(
            ["עונה 1", "עונה 2", "עונה 3", "עונה 4", "עונה 5", "עונה 6"],
            page.Seasons.Select(season => season.Title));
        Assert.Equal(
            [
                "/mako-vod-keshet/shemesh-s1",
                "/mako-vod-keshet/shemesh-s2",
                "/mako-vod-keshet/shemesh-s3",
                "/mako-vod-keshet/shemesh-s4",
                "/mako-vod-keshet/shemesh-s5",
                "/mako-vod-keshet/shemesh-s6",
            ],
            page.Seasons.Select(season => season.PageUrl));
    }

    [Fact]
    public void TheDisplayedSeasonIdComesFromTheSeasonMarkedCurrent()
    {
        ShowPage page = MakoPages.ParseShowPage(ShowPageJson);

        Assert.Equal("/mako-vod-keshet/shemesh-s6", page.DisplayedSeasonId);
    }

    [Fact]
    public void TheOverviewIsTheHerosDescription()
    {
        ShowPage page = MakoPages.ParseShowPage(ShowPageJson);

        Assert.StartsWith("העלילה מתמקדת", page.Overview, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePosterIsTheHerosFirstPicture()
    {
        ShowPage page = MakoPages.ParseShowPage(ShowPageJson);

        Assert.Equal("https://img.mako.co.il/2025/06/09/Shemesh_e_nologo.jpg", page.PosterUrl);
    }

    [Fact]
    public void ASeasonTitleWithNoIntegerIsNumberedByItsPosition()
    {
        const string pageJson = """
            {
                "pageUrl": "/show/x",
                "seasons": [
                    { "seasonTitle": "עונה מיוחדת", "current": false, "pageUrl": "/show/special" },
                    { "seasonTitle": "עונה 2", "current": true, "pageUrl": "/show/s2" }
                ]
            }
            """;

        ShowPage page = MakoPages.ParseShowPage(pageJson);

        Assert.Equal(1, page.Seasons[0].Number);
        Assert.Equal(2, page.Seasons[1].Number);
    }

    [Fact]
    public void APageWithNoSeasonsArrayIsItsOwnSingleCurrentSeason()
    {
        const string pageJson = """{ "pageUrl": "/show/only" }""";

        ShowPage page = MakoPages.ParseShowPage(pageJson);

        MakoSeason season = Assert.Single(page.Seasons);
        Assert.Equal("/show/only", season.PageUrl);
        Assert.Equal(1, season.Number);
        Assert.Equal("עונה 1", season.Title);
        Assert.Equal("/show/only", page.DisplayedSeasonId);
    }

    [Fact]
    public void ParsesTheFourRealEpisodesOfTheSeasonPageSkippingTheAdSlot()
    {
        IReadOnlyList<Episode> episodes = EpisodesOf(SeasonOnePageJson);

        Assert.Equal(4, episodes.Count);
    }

    [Fact]
    public void TheFirstEpisodeCarriesEveryFieldParsedFromTheFixture()
    {
        IReadOnlyList<Episode> episodes = EpisodesOf(SeasonOnePageJson);

        Episode first = episodes[0];
        Assert.Equal("שמש בעל הנס", first.Name);
        Assert.Equal(1, first.Number);
        Assert.Equal(1, first.SeasonNumber);
        Assert.Equal(ShowName, first.ShowName);
        Assert.Equal(new DateTime(1997, 12, 25, 0, 0, 0, DateTimeKind.Utc), first.AirDate);
        Assert.Equal(
            "https://img.mako.co.il/2025/03/04/Shemesh_01_251297_VOD_autoOrient._autoOrient_i.jpg",
            first.ThumbnailUrl);
        Assert.Null(first.Duration);
        Assert.Equal(
            new EpisodeKey(
                "d13f539206c35910VgnVCM200000650a10acRCRD",
                "4e091bfd191d0810VgnVCM200000650a10acRCRD",
                "d13f539206c35910VgnVCM200000650a10acRCRD").ToString(),
            first.Id);
    }

    [Fact]
    public void EpisodeNumberFallsBackToPositionWhenExtraInfoHasNoPerekMarker()
    {
        const string pageJson = """
            {
                "menu": [
                    {
                        "id": "allEpisodes",
                        "vods": [
                            {
                                "componentLayout": "vod",
                                "title": "פרק ללא מספר",
                                "itemVcmId": "vcm-1",
                                "channelId": "chan-1",
                                "galleryChannelId": "gal-1",
                                "extraInfo": "@01.02.20"
                            },
                            {
                                "componentLayout": "vod",
                                "title": "פרק עם מספר",
                                "itemVcmId": "vcm-2",
                                "channelId": "chan-2",
                                "galleryChannelId": "gal-2",
                                "extraInfo": "פרק 5@02.02.20"
                            }
                        ]
                    }
                ]
            }
            """;

        IReadOnlyList<Episode> episodes = EpisodesOf(pageJson);

        Assert.Equal(1, episodes[0].Number);
        Assert.Equal(5, episodes[1].Number);
    }

    [Theory]
    [InlineData("@01.01.70", 1970)]
    [InlineData("@01.01.99", 1999)]
    [InlineData("@01.01.69", 2069)]
    [InlineData("@01.01.00", 2000)]
    public void AirDateTwoDigitYearPivotsOnSeventy(string extraInfo, int expectedYear)
    {
        string pageJson = SingleEpisodePageJson(extraInfo);

        IReadOnlyList<Episode> episodes = EpisodesOf(pageJson);

        Assert.Equal(expectedYear, episodes[0].AirDate!.Value.Year);
    }

    [Fact]
    public void AnUnparseableAirDateBecomesNull()
    {
        string pageJson = SingleEpisodePageJson("אין תאריך כאן");

        IReadOnlyList<Episode> episodes = EpisodesOf(pageJson);

        Assert.Null(episodes[0].AirDate);
    }

    [Fact]
    public void AnEntryMissingAnyOfTheThreePlaybackIdsIsSkipped()
    {
        const string pageJson = """
            {
                "menu": [
                    {
                        "id": "allEpisodes",
                        "vods": [
                            {
                                "componentLayout": "vod",
                                "title": "חסר מזהה",
                                "itemVcmId": "vcm-1",
                                "galleryChannelId": "gal-1",
                                "extraInfo": "פרק 1@01.01.20"
                            },
                            {
                                "componentLayout": "vod",
                                "title": "תקין",
                                "itemVcmId": "vcm-2",
                                "channelId": "chan-2",
                                "galleryChannelId": "gal-2",
                                "extraInfo": "פרק 2@02.01.20"
                            }
                        ]
                    }
                ]
            }
            """;

        IReadOnlyList<Episode> episodes = EpisodesOf(pageJson);

        Episode kept = Assert.Single(episodes);
        Assert.Equal("תקין", kept.Name);
    }

    [Fact]
    public void WithNoAllEpisodesTabTheFirstTabWithVodsIsUsed()
    {
        const string pageJson = """
            {
                "menu": [
                    { "id": "credits" },
                    {
                        "id": "somethingElse",
                        "vods": [
                            {
                                "componentLayout": "vod",
                                "title": "פרק יחיד",
                                "itemVcmId": "vcm-1",
                                "channelId": "chan-1",
                                "galleryChannelId": "gal-1",
                                "extraInfo": "פרק 1@01.01.20"
                            }
                        ]
                    }
                ]
            }
            """;

        IReadOnlyList<Episode> episodes = EpisodesOf(pageJson);

        Episode kept = Assert.Single(episodes);
        Assert.Equal("פרק יחיד", kept.Name);
    }

    /// <summary>
    /// The season-1 fixture's own <c>seasons[]</c> marks the season the PAGE IS SHOWING as
    /// <c>current: true</c> (that would be season 1, since this is season 1's page) — but the
    /// same fixture's <c>seasons[]</c> also lists seasons up to 6, so the LATEST (highest
    /// numbered) season on this page is season 6, not season 1.
    /// </summary>
    [Fact]
    public void IsLatestSeasonIsFalseForTheDisplayedSeasonWhenAHigherNumberedSeasonExists()
    {
        bool isLatest = MakoPages.IsLatestSeason(SeasonOnePageJson, "/mako-vod-keshet/shemesh-s1");

        Assert.False(isLatest);
    }

    [Fact]
    public void IsLatestSeasonIsTrueForTheHighestNumberedSeasonInTheSamePage()
    {
        bool isLatest = MakoPages.IsLatestSeason(SeasonOnePageJson, "/mako-vod-keshet/shemesh-s6");

        Assert.True(isLatest);
    }

    [Fact]
    public void IsLatestSeasonIsFalseForASeasonThatIsNeitherDisplayedNorHighestNumbered()
    {
        bool isLatest = MakoPages.IsLatestSeason(SeasonOnePageJson, "/mako-vod-keshet/shemesh-s2");

        Assert.False(isLatest);
    }

    [Fact]
    public void IsLatestSeasonIsTrueWhenThePageHasNoSeasonsArray()
    {
        bool isLatest = MakoPages.IsLatestSeason("""{ "pageUrl": "/show/only" }""", "/show/only");

        Assert.True(isLatest);
    }

    [Fact]
    public void MalformedJsonFailsWithAFormatException()
    {
        FormatException exception = Assert.Throws<FormatException>(() => MakoPages.ParseShowPage("not json"));

        Assert.Equal("mako page is not valid JSON", exception.Message);
    }

    [Fact]
    public void ParseEpisodesFailsWithAFormatExceptionOnMalformedJson()
    {
        FormatException exception = Assert.Throws<FormatException>(
            () => MakoPages.ParseEpisodes("not json"));

        Assert.Equal("mako page is not valid JSON", exception.Message);
    }

    [Fact]
    public void IsLatestSeasonFailsWithAFormatExceptionOnMalformedJson()
    {
        FormatException exception = Assert.Throws<FormatException>(
            () => MakoPages.IsLatestSeason("not json", "/show/s1"));

        Assert.Equal("mako page is not valid JSON", exception.Message);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("42")]
    public void ParseShowPageFailsWithAFormatExceptionWhenTheRootIsNotAJsonObject(string nonObjectJson)
    {
        FormatException exception = Assert.Throws<FormatException>(() => MakoPages.ParseShowPage(nonObjectJson));

        Assert.Equal("mako page is not a JSON object", exception.Message);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("42")]
    public void ParseEpisodesFailsWithAFormatExceptionWhenTheRootIsNotAJsonObject(string nonObjectJson)
    {
        FormatException exception = Assert.Throws<FormatException>(
            () => MakoPages.ParseEpisodes(nonObjectJson));

        Assert.Equal("mako page is not a JSON object", exception.Message);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("42")]
    public void IsLatestSeasonFailsWithAFormatExceptionWhenTheRootIsNotAJsonObject(string nonObjectJson)
    {
        FormatException exception = Assert.Throws<FormatException>(
            () => MakoPages.IsLatestSeason(nonObjectJson, "/show/s1"));

        Assert.Equal("mako page is not a JSON object", exception.Message);
    }

    [Fact]
    public void NonObjectEntriesInSeasonsAreSkippedWithoutThrowing()
    {
        const string pageJson = """
            {
                "pageUrl": "/show/x",
                "seasons": ["x", 42, null, true, { "seasonTitle": "עונה 1", "current": true, "pageUrl": "/show/s1" }]
            }
            """;

        ShowPage page = MakoPages.ParseShowPage(pageJson);

        MakoSeason season = Assert.Single(page.Seasons);
        Assert.Equal("/show/s1", season.PageUrl);
        Assert.Equal(1, season.Number);
        Assert.Equal("עונה 1", season.Title);
        Assert.Equal("/show/s1", page.DisplayedSeasonId);
    }

    [Fact]
    public void NonObjectVodsEntriesAreSkippedWithoutThrowing()
    {
        const string pageJson = """
            {
                "menu": [
                    {
                        "id": "allEpisodes",
                        "vods": [
                            null,
                            "x",
                            42,
                            {
                                "componentLayout": "vod",
                                "title": "תקין",
                                "itemVcmId": "vcm-1",
                                "channelId": "chan-1",
                                "galleryChannelId": "gal-1",
                                "extraInfo": "פרק 1@01.01.20"
                            }
                        ]
                    }
                ]
            }
            """;

        IReadOnlyList<Episode> episodes = EpisodesOf(pageJson);

        Episode kept = Assert.Single(episodes);
        Assert.Equal("תקין", kept.Name);
    }

    [Fact]
    public void PosterIsNullWhenHeroPicsIsAnObjectInsteadOfAnArray()
    {
        const string pageJson = """{ "hero": { "description": "x", "pics": { "not": "an array" } } }""";

        ShowPage page = MakoPages.ParseShowPage(pageJson);

        Assert.Null(page.PosterUrl);
    }

    [Fact]
    public void PosterIsNullWhenHeroPicsIsAStringInsteadOfAnArray()
    {
        const string pageJson = """{ "hero": { "description": "x", "pics": "not an array" } }""";

        ShowPage page = MakoPages.ParseShowPage(pageJson);

        Assert.Null(page.PosterUrl);
    }

    [Fact]
    public void ThumbnailIsNullWhenAnEpisodesPicsIsAStringInsteadOfAnArray()
    {
        const string pageJson = """
            {
                "menu": [
                    {
                        "id": "allEpisodes",
                        "vods": [
                            {
                                "componentLayout": "vod",
                                "title": "פרק",
                                "itemVcmId": "vcm-1",
                                "channelId": "chan-1",
                                "galleryChannelId": "gal-1",
                                "pics": "not an array"
                            }
                        ]
                    }
                ]
            }
            """;

        IReadOnlyList<Episode> episodes = EpisodesOf(pageJson);

        Assert.Null(episodes[0].ThumbnailUrl);
    }

    [Fact]
    public void ExtraInfoAsANumberFallsBackToPositionWithNoAirDate()
    {
        const string pageJson = """
            {
                "menu": [
                    {
                        "id": "allEpisodes",
                        "vods": [
                            {
                                "componentLayout": "vod",
                                "title": "פרק",
                                "itemVcmId": "vcm-1",
                                "channelId": "chan-1",
                                "galleryChannelId": "gal-1",
                                "extraInfo": 5
                            }
                        ]
                    }
                ]
            }
            """;

        IReadOnlyList<Episode> episodes = EpisodesOf(pageJson);

        Assert.Equal(1, episodes[0].Number);
        Assert.Null(episodes[0].AirDate);
    }

    [Fact]
    public void SeasonTitleAsANumberIsNumberedByItsPosition()
    {
        const string pageJson = """
            {
                "pageUrl": "/show/x",
                "seasons": [{ "seasonTitle": 6, "pageUrl": "/show/s6" }]
            }
            """;

        ShowPage page = MakoPages.ParseShowPage(pageJson);

        Assert.Equal(1, page.Seasons[0].Number);
    }

    private static string SingleEpisodePageJson(string extraInfo) =>
        $$"""
        {
            "menu": [
                {
                    "id": "allEpisodes",
                    "vods": [
                        {
                            "componentLayout": "vod",
                            "title": "פרק בדיקה",
                            "itemVcmId": "vcm-1",
                            "channelId": "chan-1",
                            "galleryChannelId": "gal-1",
                            "extraInfo": "{{extraInfo}}"
                        }
                    ]
                }
            ]
        }
        """;

    /// <summary>A season page's episodes as the channel gets them: parsed by
    /// <see cref="MakoPages.ParseEpisodes"/>, then converted by <see cref="MakoTransformer"/> — the
    /// episode number and air date are read off mako's <c>extraInfo</c> in that second step.</summary>
    private static IReadOnlyList<Episode> EpisodesOf(string pageJson) =>
        new MakoTransformer().ToEpisodes(new MakoSeasonPage(ShowName, 1, MakoPages.ParseEpisodes(pageJson)));
}
