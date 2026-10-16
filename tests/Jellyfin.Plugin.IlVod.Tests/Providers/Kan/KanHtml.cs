namespace Jellyfin.Plugin.IlVod.Tests.Providers.Kan;

/// <summary>Small Kan pages written inline, in the fixtures' markup, for the cases the recorded
/// pages do not show: duplicate and out-of-scope cards, short walks, a lobby of chosen rails.</summary>
public static class KanHtml
{
    public const string Kan11Section = "kan/kan-11";
    public const string ArchiveSection = "archive1/vod";

    public static string Lobby(params (string Name, string Slug)[] rails) =>
        string.Concat(rails.Select(rail => $"""
            <h2 class="h3 title-elem" style="color: ">
                <a href="https://www.kan.org.il/tags/content-category-tags/{rail.Slug}/" class="unstyled-link"> {rail.Name} </a>
            </h2>
            """));

    public static string LobbyCard(string programId, string poster, string synopsis) => $"""
        <a href="https://www.kan.org.il/content/kan/kan-11/p-{programId}/" class="card-link d-inline-block">
            <img src="{poster}?width=300&amp;height=450" />
            <h3 class="mb-3">{synopsis}</h3>
        </a>
        """;

    public static string GenrePage(int? statedTotal, int? lastPage, params string[] cards)
    {
        string total = statedTotal is int count ? $"""<div class="result-number">מציג {count} תוצאות</div>""" : string.Empty;
        string paging = lastPage is int last
            ? $"""<li class="pagination-page__item" data-num="1" title="First page"></li><li class="pagination-page__item" data-num="{last}" title="Last page"></li>"""
            : string.Empty;
        return $"<html>{total}<ul>{string.Concat(cards)}</ul>{paging}</html>";
    }

    public static string Card(string programId, string section = Kan11Section, string? image = "/media/x/1920x1080.jpg") => $"""
        <li class="grid-list-item"><div class="card">
            <a href="https://www.kan.org.il/content/{section}/p-{programId}/" class="card-media img" data-item-type="program" data-item-id="{programId}">
                {(image is null ? string.Empty : $"""<img src="{image}?width=288&amp;height=162" alt="show {programId}" />""")}
            </a>
            <a href="https://www.kan.org.il/content/{section}/p-{programId}/" data-item-id="{programId}"><h4 class="card-title">show {programId}</h4></a>
        </div></li>
        """;
}
