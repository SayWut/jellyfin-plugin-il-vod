using Jellyfin.Plugin.Mako.MakoApi;
using Jellyfin.Plugin.Mako.Model;
using Jellyfin.Plugin.Mako.Tests.Support;

namespace Jellyfin.Plugin.Mako.Tests.Mako;

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
            page.Seasons.Select(season => season.Name));
        Assert.Equal(
            [
                "/mako-vod-keshet/shemesh-s1",
                "/mako-vod-keshet/shemesh-s2",
                "/mako-vod-keshet/shemesh-s3",
                "/mako-vod-keshet/shemesh-s4",
                "/mako-vod-keshet/shemesh-s5",
                "/mako-vod-keshet/shemesh-s6",
            ],
            page.Seasons.Select(season => season.Id));
    }

    [Fact]
    public void TheCurrentSeasonIdComesFromTheSeasonMarkedCurrent()
    {
        ShowPage page = MakoPages.ParseShowPage(ShowPageJson);

        Assert.Equal("/mako-vod-keshet/shemesh-s6", page.CurrentSeasonId);
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

        Season season = Assert.Single(page.Seasons);
        Assert.Equal("/show/only", season.Id);
        Assert.Equal(1, season.Number);
        Assert.Equal("עונה 1", season.Name);
        Assert.Equal("/show/only", page.CurrentSeasonId);
    }

    [Fact]
    public void ParsesTheFourRealEpisodesOfTheSeasonPageSkippingTheAdSlot()
    {
        IReadOnlyList<Episode> episodes = MakoPages.ParseEpisodes(SeasonOnePageJson, ShowName, 1);

        Assert.Equal(4, episodes.Count);
    }

    [Fact]
    public void TheFirstEpisodeCarriesEveryFieldParsedFromTheFixture()
    {
        IReadOnlyList<Episode> episodes = MakoPages.ParseEpisodes(SeasonOnePageJson, ShowName, 1);

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

        IReadOnlyList<Episode> episodes = MakoPages.ParseEpisodes(pageJson, ShowName, 1);

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

        IReadOnlyList<Episode> episodes = MakoPages.ParseEpisodes(pageJson, ShowName, 1);

        Assert.Equal(expectedYear, episodes[0].AirDate!.Value.Year);
    }

    [Fact]
    public void AnUnparseableAirDateBecomesNull()
    {
        string pageJson = SingleEpisodePageJson("אין תאריך כאן");

        IReadOnlyList<Episode> episodes = MakoPages.ParseEpisodes(pageJson, ShowName, 1);

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

        IReadOnlyList<Episode> episodes = MakoPages.ParseEpisodes(pageJson, ShowName, 1);

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

        IReadOnlyList<Episode> episodes = MakoPages.ParseEpisodes(pageJson, ShowName, 1);

        Episode kept = Assert.Single(episodes);
        Assert.Equal("פרק יחיד", kept.Name);
    }

    [Fact]
    public void IsCurrentSeasonIsTrueForTheSeasonMarkedCurrent()
    {
        bool isCurrent = MakoPages.IsCurrentSeason(SeasonOnePageJson, "/mako-vod-keshet/shemesh-s1");

        Assert.True(isCurrent);
    }

    [Fact]
    public void IsCurrentSeasonIsFalseForAnotherSeasonInTheSamePage()
    {
        bool isCurrent = MakoPages.IsCurrentSeason(SeasonOnePageJson, "/mako-vod-keshet/shemesh-s2");

        Assert.False(isCurrent);
    }

    [Fact]
    public void IsCurrentSeasonIsTrueWhenThePageHasNoSeasonsArray()
    {
        bool isCurrent = MakoPages.IsCurrentSeason("""{ "pageUrl": "/show/only" }""", "/show/only");

        Assert.True(isCurrent);
    }

    [Fact]
    public void MalformedJsonFailsWithAFormatException()
    {
        FormatException exception = Assert.Throws<FormatException>(() => MakoPages.ParseShowPage("not json"));

        Assert.Equal("mako page is not valid JSON", exception.Message);
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
}
