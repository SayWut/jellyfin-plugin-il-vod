using Jellyfin.Plugin.IlVod.Providers.Kan.Source;
using Jellyfin.Plugin.IlVod.Tests.Support;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Kan;

public class KanPlayerConfigTests
{
    public const string MasterUrl =
        "https://r.il.cdn-redge.media/webcache/gorigin/hls/oil/kancdn/vod/2253468/LIBCODER_SMOOTH_1080_KAN/Manifest.ism/playlist.m3u8?fmp4";

    [Fact]
    public void TheEpisodePagesPlayerNamesItsMasterPlaylist() =>
        Assert.Equal(MasterUrl, KanPlayerConfig.ParseHlsUrl(FixtureFile.ReadText("kan-episode-page.html"), "1107310"));

    [Fact]
    public void AnAbsoluteRedgeUrlIsKept() =>
        Assert.Equal("https://x.cdn-redge.media/a.m3u8", KanPlayerConfig.ParseHlsUrl(Page("https://x.cdn-redge.media/a.m3u8"), "1"));

    [Theory]
    [InlineData("//evil.example/a.m3u8")]
    [InlineData("//cdn-redge.media.evil.example/a.m3u8")]
    [InlineData("http://r.il.cdn-redge.media/a.m3u8")]
    [InlineData("")]
    public void AnythingButAnHttpsRedgeUrlIsRefused(string hls) =>
        Assert.Throws<FormatException>(() => KanPlayerConfig.ParseHlsUrl(Page(hls), "1"));

    [Theory]
    [InlineData("<html><body>no player here</body></html>")]
    [InlineData("""<script type="application/json" data-redge-config="x">not json</script>""")]
    [InlineData("""<script type="application/json" data-redge-config="x">{"file":null}</script>""")]
    public void APageWithNoUsablePlayerIsNotAnEpisodePage(string html) =>
        Assert.Throws<FormatException>(() => KanPlayerConfig.ParseHlsUrl(html, "1"));

    /// <summary>A page whose only player is another video's (a promo, or a redirect that landed on
    /// another episode) must not play in this episode's place.</summary>
    [Fact]
    public void APlayerForAnotherEntryIsNotThisEpisodes() =>
        Assert.Throws<FormatException>(() => KanPlayerConfig.ParseHlsUrl(FixtureFile.ReadText("kan-episode-page.html"), "1107309"));

    [Fact]
    public void ThisEpisodesPlayerIsFoundAfterAnotherOne() =>
        Assert.Equal(
            "https://x.cdn-redge.media/b.m3u8",
            KanPlayerConfig.ParseHlsUrl(Page("https://x.cdn-redge.media/a.m3u8", "2") + Page("https://x.cdn-redge.media/b.m3u8", "1"), "1"));

    private static string Page(string hls, string entryId = "1") =>
        "<html><script type=\"application/json\" data-redge-config=\"redge-player-" + entryId + "\">{\"id\":\"" + entryId
        + "\",\"file\":{\"hls\":\"" + hls + "\"}}</script></html>";
}
