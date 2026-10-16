using System.Globalization;

namespace Jellyfin.Plugin.IlVod.Providers.Kan;

/// <summary>
/// A season's id: its <see cref="KanEntry.Season"/> number as text, or <see cref="NoSeason"/> for
/// the episodes that state none. Both halves need the one spelling — the transformer hands it to
/// Jellyfin, the source filters a show's episodes by it — so it lives here, beside the records.
/// </summary>
public static class KanSeasonId
{
    /// <summary>What Kan's own feed numbers these episodes; no real season is ever 0.</summary>
    public const string NoSeason = "0";

    public static string Of(int? season) =>
        season?.ToString(CultureInfo.InvariantCulture) ?? NoSeason;

    /// <exception cref="FormatException"><paramref name="seasonId"/> is no whole number.</exception>
    public static int? Parse(string seasonId)
    {
        if (!int.TryParse(seasonId, NumberStyles.None, CultureInfo.InvariantCulture, out int season))
        {
            throw new FormatException($"kan season id '{seasonId}' is not a season number");
        }

        return season == 0 ? null : season;
    }
}
