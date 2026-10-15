namespace Jellyfin.Plugin.IlVod.Providers.Mako;

/// <summary>
/// What <see cref="MakoPages.ParseShowPage"/> reads off a mako show page: enough to fill the
/// show's folder and to know which season's episodes the same page already carries.
/// </summary>
/// <param name="DisplayedSeasonId">
/// The id of the season flagged <c>current: true</c> in <c>seasons[]</c> — the season whose
/// episodes THIS page's own JSON contains. This is the season the page is showing, which is not
/// necessarily the season currently airing (see <see cref="MakoPages.IsLatestSeason"/>): a page
/// for a past season marks that same past season as its own "current" (displayed) one.
/// </param>
public sealed record ShowPage(string? Overview, string? PosterUrl, IReadOnlyList<MakoSeason> Seasons, string? DisplayedSeasonId);
