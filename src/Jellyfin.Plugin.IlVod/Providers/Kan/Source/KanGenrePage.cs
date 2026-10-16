namespace Jellyfin.Plugin.IlVod.Providers.Kan.Source;

/// <summary>One page of a genre's programs, 30 at most.</summary>
/// <param name="StatedTotal">How many programs the genre says it has across all its pages;
/// <c>null</c> when the page does not say.</param>
/// <param name="LastPage">The genre's last page number, counted from 1.</param>
public sealed record KanGenrePage(IReadOnlyList<KanGenreCard> Cards, int StatedTotal, int LastPage);
