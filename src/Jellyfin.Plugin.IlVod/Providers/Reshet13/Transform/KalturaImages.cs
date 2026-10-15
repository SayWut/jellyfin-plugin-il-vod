using System.Globalization;

namespace Jellyfin.Plugin.IlVod.Providers.Reshet13.Transform;

/// <summary>
/// Picks which of an asset's images Jellyfin shows, and the sized URL to fetch it from. Kaltura
/// serves an image at whatever size the URL asks for (<c>/width/{w}/height/{h}/quality/{q}</c>
/// appended to the base URL), so each ratio is asked for at a size of its own shape — anything else
/// would come back stretched.
/// </summary>
public static class KalturaImages
{
    private const int Quality = 80;

    private static readonly ImageSize PortraitTwoByThree = new("2x3", 400, 600);
    private static readonly ImageSize PortraitNineBySixteen = new("9x16", 405, 720);
    private static readonly ImageSize Wide = new("16x9", 640, 360);

    /// <summary>Not every series has a 2x3 poster (מסיבת בריכה has only 16x9 and 9x16), so the
    /// tall 9x16 is next best, and a wide image beats none.</summary>
    private static readonly ImageSize[] PosterPreference = [PortraitTwoByThree, PortraitNineBySixteen, Wide];

    /// <summary>Episodes carry only 16x9 images.</summary>
    private static readonly ImageSize[] ThumbnailPreference = [Wide];

    /// <returns><c>null</c> when the series has no image of a usable ratio.</returns>
    public static string? PosterUrl(IReadOnlyList<KalturaImage> images) => PreferredUrl(images, PosterPreference);

    /// <returns><c>null</c> when the episode has no 16x9 image.</returns>
    public static string? ThumbnailUrl(IReadOnlyList<KalturaImage> images) => PreferredUrl(images, ThumbnailPreference);

    private static string? PreferredUrl(IReadOnlyList<KalturaImage> images, IEnumerable<ImageSize> preference)
    {
        foreach (ImageSize size in preference)
        {
            KalturaImage? image = images.FirstOrDefault(candidate => candidate.Ratio == size.Ratio);
            if (image is not null)
            {
                return SizedUrl(image.Url, size);
            }
        }

        return null;
    }

    private static string SizedUrl(string baseUrl, ImageSize size) =>
        string.Create(CultureInfo.InvariantCulture, $"{baseUrl}/width/{size.Width}/height/{size.Height}/quality/{Quality}");

    private sealed record ImageSize(string Ratio, int Width, int Height);
}
