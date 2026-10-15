using Jellyfin.Plugin.IlVod.Net;

namespace Jellyfin.Plugin.IlVod.Tests.Net;

public class SafeUrlTests
{
    [Fact]
    public void ARedactedUrlLosesItsQueryString()
    {
        string redacted = SafeUrl.Redacted("https://www.mako.co.il/mako-vod-keshet/shemesh?hdnea=SECRET&platform=mobileApp");

        Assert.Equal("www.mako.co.il/mako-vod-keshet/shemesh", redacted);
    }

    [Fact]
    public void ARedactedSignedUrlKeepsOnlyItsHostAndPath()
    {
        string redacted = SafeUrl.Redacted("https://cfvod.kaltura.com/a/index.m3u8?Policy=SECRET&Signature=SECRET");

        Assert.Equal("cfvod.kaltura.com/a/index.m3u8", redacted);
    }

    [Fact]
    public void ARedactedUrlLosesItsFragment()
    {
        string redacted = SafeUrl.Redacted("https://www.mako.co.il/mako-vod-keshet/shemesh#player");

        Assert.Equal("www.mako.co.il/mako-vod-keshet/shemesh", redacted);
    }

    [Fact]
    public void AMalformedUrlRedactsToThePlaceholderAndNeverThrows()
    {
        string redacted = SafeUrl.Redacted("not a url at all");

        Assert.Equal("<unparseable url>", redacted);
    }
}
