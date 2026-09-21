using Jellyfin.Plugin.Mako.Model;

namespace Jellyfin.Plugin.Mako.MakoApi;

/// <summary>
/// What <see cref="MakoPages.ParseShowPage"/> reads off a mako show page: enough to fill the
/// show's folder and to know which season's episodes the same page already carries.
/// </summary>
public sealed record ShowPage(string? Overview, string? PosterUrl, IReadOnlyList<Season> Seasons, string? CurrentSeasonId);
