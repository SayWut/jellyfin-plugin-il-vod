using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.IlVod.Providers.Kan.Source;

/// <summary>
/// Reads Kan BOX's HTML pages — the lobby and a genre page — by their markup, which is all they
/// offer: Kan publishes no API listing its catalogue. A page that does not have the markup looked
/// for (a Cloudflare challenge, a redesign) is no page, never an empty one.
/// </summary>
public static partial class KanPages
{
    private const string GenreCardMarker = """<li class="grid-list-item">""";
    private const string LastPageTitle = "title=\"Last page\"";

    /// <exception cref="FormatException"><paramref name="html"/> lists no genre rail.</exception>
    public static KanLobby ParseLobby(string html)
    {
        List<KanRail> rails = [.. RailHeading().Matches(html).Select(ToRail).DistinctBy(rail => rail.Slug)];
        if (rails.Count == 0)
        {
            throw new FormatException("kan lobby lists no genre rail");
        }

        return new KanLobby(rails, LobbyCardsByProgramId(html));
    }

    /// <exception cref="FormatException"><paramref name="html"/> has neither cards nor a stated
    /// total, or has a card without a program's id, link or name.</exception>
    public static KanGenrePage ParseGenrePage(string html)
    {
        List<KanGenreCard> cards = [.. html.Split(GenreCardMarker).Skip(1).Select(ToGenreCard).DistinctBy(card => card.ProgramId)];
        int? statedTotal = StatedTotalOf(html);
        if (cards.Count == 0 && statedTotal is null)
        {
            throw new FormatException("kan genre page lists no programs and states no total");
        }

        return new KanGenrePage(cards, statedTotal, LastPageOf(html));
    }

    private static KanRail ToRail(Match heading)
    {
        string slug = heading.Groups["slug"].Value;
        return KanUrls.IsGenreSlug(slug)
            ? new KanRail(Decoded(heading.Groups["name"].Value), slug)
            : throw new FormatException($"kan lobby rail '{slug}' links to no genre page");
    }

    /// <summary>A program on several rails has the same poster and synopsis on each; the first is kept.</summary>
    private static Dictionary<string, KanLobbyCard> LobbyCardsByProgramId(string html)
    {
        var cards = new Dictionary<string, KanLobbyCard>(StringComparer.Ordinal);
        foreach (Match card in LobbyCard().Matches(html))
        {
            if (ProgramIdIn(card.Groups["href"].Value) is string programId)
            {
                cards.TryAdd(programId, ToLobbyCard(card.Groups["body"].Value));
            }
        }

        return cards;
    }

    private static KanLobbyCard ToLobbyCard(string cardHtml)
    {
        string synopsis = Decoded(Synopsis().Match(cardHtml).Groups["text"].Value);
        return new KanLobbyCard(ImageUrlIn(cardHtml), synopsis.Length > 0 ? synopsis : null);
    }

    private static KanGenreCard ToGenreCard(string cardHtml)
    {
        Match link = GenreCardLink().Match(cardHtml);
        string name = Decoded(GenreCardTitle().Match(cardHtml).Groups["name"].Value);
        if (!link.Success || name.Length == 0)
        {
            throw new FormatException("kan genre page has a card without a program's link or name");
        }

        return new KanGenreCard(link.Groups["id"].Value, name, Decoded(link.Groups["href"].Value), ImageUrlIn(cardHtml));
    }

    private static int? StatedTotalOf(string html) =>
        StatedTotal().Match(html) is { Success: true } total
            ? int.Parse(total.Groups["total"].Value, NumberStyles.None, CultureInfo.InvariantCulture)
            : null;

    /// <summary>A genre that fits on one page shows no paging at all.</summary>
    private static int LastPageOf(string html) =>
        PagingItem().Matches(html)
            .Select(item => item.Groups["attributes"].Value)
            .Where(attributes => attributes.Contains(LastPageTitle, StringComparison.Ordinal))
            .Select(attributes => PageNumber().Match(attributes))
            .Where(number => number.Success)
            .Select(number => int.Parse(number.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture))
            .FirstOrDefault(1);

    private static string? ProgramIdIn(string href) =>
        ProgramIdInHref().Match(href) is { Success: true } match ? match.Groups["id"].Value : null;

    private static string? ImageUrlIn(string html) =>
        ImageSource().Match(html) is { Success: true } image ? KanUrls.Absolute(Decoded(image.Groups["src"].Value)) : null;

    private static string Decoded(string html) => WebUtility.HtmlDecode(html).Trim();

    [GeneratedRegex("""<h2 class="h3 title-elem"[^>]*>\s*<a href="[^"]*/tags/content-category-tags/(?<slug>[^/"]+)/"[^>]*>(?<name>[^<]*)</a>""")]
    private static partial Regex RailHeading();

    [GeneratedRegex("""<a href="(?<href>[^"]*)" class="card-link d-inline-block">(?<body>.*?)</a>""", RegexOptions.Singleline)]
    private static partial Regex LobbyCard();

    [GeneratedRegex("""<h3 class="mb-3">(?<text>.*?)</h3>""", RegexOptions.Singleline)]
    private static partial Regex Synopsis();

    [GeneratedRegex(@"<a href=""(?<href>[^""]*)""[^>]*\bdata-item-id=""(?<id>\d+)""")]
    private static partial Regex GenreCardLink();

    [GeneratedRegex("""<h4 class="card-title">(?<name>.*?)</h4>""", RegexOptions.Singleline)]
    private static partial Regex GenreCardTitle();

    [GeneratedRegex(@"<img[^>]*\bsrc=""(?<src>[^""]+)""")]
    private static partial Regex ImageSource();

    [GeneratedRegex(@"/p-(?<id>\d+)/")]
    private static partial Regex ProgramIdInHref();

    [GeneratedRegex(@"(?<total>\d+)\s*תוצאות")]
    private static partial Regex StatedTotal();

    [GeneratedRegex("""<li class="pagination-page__item[^"]*"(?<attributes>[^>]*)>""")]
    private static partial Regex PagingItem();

    [GeneratedRegex(@"data-num=""(?<number>\d+)""")]
    private static partial Regex PageNumber();
}
