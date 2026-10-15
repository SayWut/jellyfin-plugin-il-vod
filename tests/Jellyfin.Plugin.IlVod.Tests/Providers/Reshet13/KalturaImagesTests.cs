using Jellyfin.Plugin.IlVod.Providers.Reshet13;
using Jellyfin.Plugin.IlVod.Providers.Reshet13.Transform;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Reshet13;

/// <summary>
/// <see cref="KalturaImages"/>: which of an asset's images becomes the poster or thumbnail, and the
/// sized URL it is fetched from. Base URLs are האח הגדול's and מסיבת בריכה's from the fixtures.
/// </summary>
public class KalturaImagesTests
{
    private const string Wide = "https://images.frp1.ott.kaltura.com/Service.svc/GetImage/p/5031/entry_id/c12495dcabed49538bd8ba94794939f9/version/803";
    private const string Tall = "https://images.frp1.ott.kaltura.com/Service.svc/GetImage/p/5031/entry_id/1e6ca3452ab54a3aa23be330b086c4dc/version/541";
    private const string Portrait = "https://images.frp1.ott.kaltura.com/Service.svc/GetImage/p/5031/entry_id/62077ca37c744da78585700571a6f6dd/version/0";
    private const string SecondWide = "https://images.frp1.ott.kaltura.com/Service.svc/GetImage/p/5031/entry_id/c4d596a9d5c644ccb3096470b27c91b7/version/0";

    [Fact]
    public void ThePosterIsTheTwoByThreeImageWhenThereIsOne()
    {
        KalturaImage[] images = [new(Wide, "16x9"), new(Tall, "9x16"), new(SecondWide, "16x9"), new(Portrait, "2x3")];

        Assert.Equal(Portrait + "/width/400/height/600/quality/80", KalturaImages.PosterUrl(images));
    }

    [Fact]
    public void ASeriesWithNoTwoByThreeImageTakesItsNineBySixteenOne()
    {
        KalturaImage[] images = [new(Wide, "16x9"), new(Tall, "9x16")];

        Assert.Equal(Tall + "/width/405/height/720/quality/80", KalturaImages.PosterUrl(images));
    }

    [Fact]
    public void ASeriesWithOnlyWideImagesTakesTheFirstOneAsItsPoster()
    {
        KalturaImage[] images = [new(Wide, "16x9"), new(SecondWide, "16x9")];

        Assert.Equal(Wide + "/width/640/height/360/quality/80", KalturaImages.PosterUrl(images));
    }

    [Fact]
    public void AThumbnailIsTheSixteenByNineImage()
    {
        KalturaImage[] images = [new(Portrait, "2x3"), new(Wide, "16x9")];

        Assert.Equal(Wide + "/width/640/height/360/quality/80", KalturaImages.ThumbnailUrl(images));
    }

    [Fact]
    public void NoImageOfAWantedRatioMeansNoUrl()
    {
        Assert.Null(KalturaImages.PosterUrl([]));
        Assert.Null(KalturaImages.PosterUrl([new KalturaImage(Portrait, "1x1")]));
        Assert.Null(KalturaImages.ThumbnailUrl([new KalturaImage(Portrait, "2x3")]));
    }
}
