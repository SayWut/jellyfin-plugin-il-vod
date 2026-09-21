using Jellyfin.Plugin.Mako.MakoApi;
using Jellyfin.Plugin.Mako.Tests.Support;

namespace Jellyfin.Plugin.Mako.Tests.Mako;

public class MakoConfigTests
{
    private static readonly string MainConfigJson = FixtureFile.ReadText("main-config.json");
    private static readonly string CoreConfigJson = FixtureFile.ReadText("core-config.json");
    private static readonly string NotesConfigJson = FixtureFile.ReadText("notes-config.json");

    [Fact]
    public void ParsesTheThreeFixtureConfigs()
    {
        MakoConfig config = MakoConfig.Parse(MainConfigJson, CoreConfigJson, NotesConfigJson);

        Assert.Equal("https://www.mako.co.il%PAGE_URL%&platform=mobileApp", config.BaseUrlTemplate);
        Assert.Equal("078E8ABF-12plus-C6550-androidF1A35D4", config.AppId);
        Assert.Equal(
            "https://www.mako.co.il/AjaxPage?jspName=playlist12.jsp&vcmid=%VCM_ID%&videoChannelId=%CHANNEL_ID%&galleryChannelId=%GALLERY_ID%&consumer=%CONSUMER%&appId=makoTV",
            config.PlaylistUrlTemplate);
        Assert.Equal("https://mass.mako.co.il/ClicksStatistics/entitlementsServicesV2.jsp?et=egt", config.TokenUrl);
        Assert.Equal(["TESTKEY-PLAYLIST-NEW-24b", "TESTKEY-PLAYLIST-OLD-24b"], config.PlaylistKeys);
        Assert.Equal("TESTKEY-TOKEN-TICKET-24b", config.TokenKey);
    }

    [Fact]
    public void CoreConfigUrlOfReadsTheMainConfigsSourceField()
    {
        string url = MakoConfig.CoreConfigUrlOf(MainConfigJson);

        Assert.Equal("https://apps.mako.co.il/mobile/config/12Plus/android/mobile/player/coreConfig.json", url);
    }

    [Fact]
    public void NotesUrlOfReadsTheCoreConfigsSourceField()
    {
        string url = MakoConfig.NotesUrlOf(CoreConfigJson);

        Assert.Equal("https://www.mako.co.il/collab/services/notesConfig.json", url);
    }

    [Fact]
    public void ASingleAvailablePlaylistKeyIsEnough()
    {
        const string notesWithOnlyTheNewKey = """
            { "notes": { "note1_n": "TESTKEY-PLAYLIST-NEW-24b", "note3": "TESTKEY-TOKEN-TICKET-24b" } }
            """;

        MakoConfig config = MakoConfig.Parse(MainConfigJson, CoreConfigJson, notesWithOnlyTheNewKey);

        Assert.Equal(["TESTKEY-PLAYLIST-NEW-24b"], config.PlaylistKeys);
    }

    [Fact]
    public void AMissingFieldIsNamedInTheError()
    {
        const string mainConfigWithoutAppId = """
            { "sources": { "baseUrl": "https://www.mako.co.il%PAGE_URL%" }, "params": {} }
            """;

        FormatException exception = Assert.Throws<FormatException>(
            () => MakoConfig.Parse(mainConfigWithoutAppId, CoreConfigJson, NotesConfigJson));

        Assert.Contains("appId", exception.Message, StringComparison.Ordinal);
    }
}
