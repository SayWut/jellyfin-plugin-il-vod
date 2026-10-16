using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.IlVod.Providers.Kan.Source;

/// <summary>
/// Every address Kan's content is fetched from, built from constants and checked inputs only.
/// Everything is read from <c>mobapi.kan.org.il</c>: the same site as <c>www.kan.org.il</c>, which
/// the cards link to, but served without the Cloudflare challenge in front of it.
/// </summary>
public static partial class KanUrls
{
    public const string Lobby = Host + "/lobby/kan-box/";

    private const string Host = "https://mobapi.kan.org.il";

    /// <summary>Kan 11's shows, and its news and current affairs (<c>kan-actual</c>); every other
    /// section (archive, kids, digital, radio, sport) is out of this channel's scope.</summary>
    private static readonly string[] InScopePathPrefixes = ["/content/kan/kan-11/", "/content/kan/kan-actual/"];

    public static string GenrePage(string slug, int page)
    {
        if (!IsGenreSlug(slug))
        {
            throw new ArgumentException($"kan genre slug '{slug}' is not an encoded path segment", nameof(slug));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        return $"{Host}/tags/content-category-tags/{slug}/?page={page}";
    }

    public static string ProgramFeed(string programId) =>
        $"{Host}/api/mobile/program?id={CheckedId(programId, nameof(programId))}";

    /// <summary>Under <c>kan-11</c> whichever section the program is in: Kan redirects to the
    /// canonical page, news under <c>kan-actual</c> included.</summary>
    public static string EpisodePage(string programId, string entryId) =>
        $"{Host}/content/kan/kan-11/p-{CheckedId(programId, nameof(programId))}/{CheckedId(entryId, nameof(entryId))}/";

    /// <summary>A slug is taken from the lobby's own links, already percent-encoded, and goes into a
    /// path as is — so it may hold nothing that could leave that path segment.</summary>
    public static bool IsGenreSlug(string slug) => GenreSlugPattern().IsMatch(slug);

    public static bool IsInScope(string cardHref) =>
        Uri.TryCreate(cardHref, UriKind.Absolute, out Uri? uri)
        && InScopePathPrefixes.Any(prefix => uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>The page's query only sizes the image for the page's own layout; without it the
    /// transformer can ask for the size it needs.</summary>
    public static string Absolute(string imagePath) =>
        new Uri(new Uri(Host), imagePath).GetLeftPart(UriPartial.Path);

    private static string CheckedId(string id, string parameterName) =>
        KanEpisodeId.IsNumericId(id) ? id : throw new ArgumentException($"kan id '{id}' is not numeric", parameterName);

    [GeneratedRegex(@"^[A-Za-z0-9%\-]+$")]
    private static partial Regex GenreSlugPattern();
}
