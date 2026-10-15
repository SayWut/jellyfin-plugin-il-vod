using Jellyfin.Plugin.IlVod.MakoApi;
using Jellyfin.Plugin.IlVod.Tests.Support;

namespace Jellyfin.Plugin.IlVod.Tests.Mako;

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

    /// <summary>
    /// A config endpoint answering HTML with status 200 must not surface the raw parser error:
    /// <c>JsonReaderException</c>'s own message quotes the payload it choked on, and mako's config
    /// chain carries decryption keys. Each of the three documents is named, no payload text is
    /// repeated, and nothing is attached as an inner exception.
    /// </summary>
    [Theory]
    [InlineData("main")]
    [InlineData("core")]
    [InlineData("notes")]
    public void AConfigDocumentThatIsNotJsonNamesItselfAndQuotesNothing(string documentName)
    {
        const string htmlErrorPage = "<html><body>Access Denied for secret-account</body></html>";

        FormatException exception = Assert.Throws<FormatException>(() => MakoConfig.Parse(
            documentName == "main" ? htmlErrorPage : MainConfigJson,
            documentName == "core" ? htmlErrorPage : CoreConfigJson,
            documentName == "notes" ? htmlErrorPage : NotesConfigJson));

        Assert.Equal($"mako {documentName} config is not valid JSON", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void CoreConfigUrlOfAMainConfigThatIsNotJsonFailsAsAMalformedMainConfig()
    {
        FormatException exception = Assert.Throws<FormatException>(() => MakoConfig.CoreConfigUrlOf("<html>nope</html>"));

        Assert.Equal("mako main config is not valid JSON", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void NotesUrlOfACoreConfigThatIsNotJsonFailsAsAMalformedCoreConfig()
    {
        FormatException exception = Assert.Throws<FormatException>(() => MakoConfig.NotesUrlOf("<html>nope</html>"));

        Assert.Equal("mako core config is not valid JSON", exception.Message);
        Assert.Null(exception.InnerException);
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
