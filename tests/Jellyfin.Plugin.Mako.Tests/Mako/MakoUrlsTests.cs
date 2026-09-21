using Jellyfin.Plugin.Mako.MakoApi;
using Jellyfin.Plugin.Mako.Tests.Support;

namespace Jellyfin.Plugin.Mako.Tests.Mako;

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
}
