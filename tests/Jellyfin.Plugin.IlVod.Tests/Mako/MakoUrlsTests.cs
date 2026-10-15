using Jellyfin.Plugin.IlVod.MakoApi;
using Jellyfin.Plugin.IlVod.Tests.Support;

namespace Jellyfin.Plugin.IlVod.Tests.Mako;

public class MakoUrlsTests
{
    private const string BaseUrlTemplate = "https://www.mako.co.il%PAGE_URL%&platform=mobileApp";

    [Fact]
    public void PageInsertsAQuestionMarkWhenTheTemplateHasNoOtherQuery()
    {
        string url = MakoUrls.Page(BaseUrlTemplate, "/mako-vod-keshet/shemesh");

        Assert.Equal("https://www.mako.co.il/mako-vod-keshet/shemesh?platform=mobileApp", url);
    }

    [Fact]
    public void PageKeepsTheFirstAmpersandWhenThePathAlreadyHasAQuery()
    {
        string url = MakoUrls.Page(BaseUrlTemplate, "/mako-vod-index?filter=menu");

        Assert.Equal("https://www.mako.co.il/mako-vod-index?filter=menu&platform=mobileApp", url);
    }

    /// <summary>
    /// Season and show ids reach <see cref="MakoUrls.Page"/> straight from a folder id a client
    /// sent, so a page path is untrusted input. Any shape that could move the request off mako's
    /// own host — where N4's pacing and the bot-challenge back-off would no longer apply — is
    /// refused outright rather than requested.
    /// </summary>
    [Theory]
    [InlineData("@evil.example/x")]
    [InlineData("/@evil.example/x")]
    [InlineData("//evil.example/x")]
    [InlineData("https://evil.example/x")]
    [InlineData("/x://evil.example")]
    [InlineData("mako-vod-keshet/shemesh")]
    [InlineData("")]
    [InlineData("/path with a space")]
    [InlineData("/path\\with-a-backslash")]
    [InlineData("/path\nwith-a-newline")]
    public void PageRefusesAPagePathThatCouldLeaveMakosHost(string pagePath)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => MakoUrls.Page(BaseUrlTemplate, pagePath));

        Assert.StartsWith("not a mako page path", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PageKeepsTheRequestOnMakosOwnHost()
    {
        string url = MakoUrls.Page(BaseUrlTemplate, "/mako-vod-keshet/shemesh-s6");

        Assert.Equal(MakoUrls.BrowseHost, new Uri(url).Host);
    }

    [Fact]
    public void PlaylistUrlFillsEveryPlaceholder()
    {
        string coreConfigJson = FixtureFile.ReadText("core-config.json");
        MakoConfig config = MakoConfig.Parse(
            FixtureFile.ReadText("main-config.json"), coreConfigJson, FixtureFile.ReadText("notes-config.json"));

        string url = MakoUrls.Playlist(config.PlaylistUrlTemplate, "vcm-1", "chan-2", "gallery-3");

        Assert.Equal(
            "https://www.mako.co.il/AjaxPage?jspName=playlist12.jsp&vcmid=vcm-1&videoChannelId=chan-2&galleryChannelId=gallery-3&consumer=mobileApp&appId=makoTV",
            url);
    }

    [Fact]
    public void MainConfigUrlCarriesTheAppVersion()
    {
        string url = MakoUrls.MainConfigUrl("43.0");

        Assert.Equal("https://apps.mako.co.il/mobile/config/12Plus/android/mobile/43.0/mainConfig.json", url);
    }

    [Fact]
    public void RedactedUrlStripsTheQueryString()
    {
        string redacted = MakoUrls.RedactedUrl("https://www.mako.co.il/mako-vod-keshet/shemesh?hdnea=SECRET&platform=mobileApp");

        Assert.Equal("www.mako.co.il/mako-vod-keshet/shemesh", redacted);
    }

    [Fact]
    public void RedactedUrlStripsTheFragment()
    {
        string redacted = MakoUrls.RedactedUrl("https://www.mako.co.il/mako-vod-keshet/shemesh#player");

        Assert.Equal("www.mako.co.il/mako-vod-keshet/shemesh", redacted);
    }

    [Fact]
    public void RedactedUrlOfAMalformedUrlIsThePlaceholderAndNeverThrows()
    {
        string redacted = MakoUrls.RedactedUrl("not a url at all");

        Assert.Equal("<unparseable url>", redacted);
    }
}
