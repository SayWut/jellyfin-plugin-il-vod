using Jellyfin.Plugin.IlVod.Providers.Kan;
using Jellyfin.Plugin.IlVod.Providers.Kan.Transform;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Kan;

public class KanImagesTests
{
    private const string Media = "https://mobapi.kan.org.il/media/";

    [Fact]
    public void SizesAnImageThroughImageSharpsQuery()
    {
        Assert.Equal(Media + "a.jpg?width=400&height=600", KanImages.Sized(Media + "a.jpg", 400, 600));
    }

    [Fact]
    public void ThePosterIsTheLobbysTwoByThreePosterFirst()
    {
        KanShowCard card = Card(imageUrl: Media + "card.jpg", posterUrl: Media + "poster.jpg");

        Assert.Equal(Media + "poster.jpg?width=400&height=600", KanImages.PosterUrl(card));
    }

    [Fact]
    public void ThePosterFallsBackToTheSixteenByNineCardImage()
    {
        KanShowCard card = Card(imageUrl: Media + "card.jpg", posterUrl: null);

        Assert.Equal(Media + "card.jpg?width=640&height=360", KanImages.PosterUrl(card));
    }

    [Fact]
    public void AShowWithNoImageHasNoPoster()
    {
        Assert.Null(KanImages.PosterUrl(Card(imageUrl: null, posterUrl: null)));
    }

    private static KanShowCard Card(string? imageUrl, string? posterUrl) =>
        new("11510", "מה שתגידו", imageUrl, posterUrl, Overview: null, []);
}
