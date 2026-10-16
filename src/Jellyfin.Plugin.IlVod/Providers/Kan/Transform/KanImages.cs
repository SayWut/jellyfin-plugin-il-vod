using System.Globalization;

namespace Jellyfin.Plugin.IlVod.Providers.Kan.Transform;

/// <summary>
/// Picks which of a show's images Jellyfin shows, and the sized URL to fetch it from. Kan's image
/// server (Umbraco ImageSharp) serves only the sizes on its own allow-list — any other answers 404,
/// and Jellyfin then has no poster at all (seen on the phone) — so each image is asked for at a size
/// Kan's own pages ask for, in its own shape: the lobby's 300×450 posters and the episode grid's
/// 490×272 stills.
/// </summary>
public static class KanImages
{
    private const int PosterWidth = 300;
    private const int PosterHeight = 450;
    private const int CardWidth = 490;
    private const int CardHeight = 272;

    /// <param name="url">An image URL with no query of its own, as the source hands them over.</param>
    public static string Sized(string url, int width, int height) =>
        string.Create(CultureInfo.InvariantCulture, $"{url}?width={width}&height={height}");

    /// <summary>The lobby's 2:3 poster when the lobby shows the program; else the 16:9 card image,
    /// since a wide image beats none.</summary>
    /// <returns><c>null</c> when the show has neither.</returns>
    public static string? PosterUrl(KanShowCard card)
    {
        if (card.PosterUrl is { } poster)
        {
            return Sized(poster, PosterWidth, PosterHeight);
        }

        return card.ImageUrl is { } image ? Sized(image, CardWidth, CardHeight) : null;
    }
}
