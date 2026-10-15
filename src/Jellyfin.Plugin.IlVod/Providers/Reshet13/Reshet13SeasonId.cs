using System.Globalization;

namespace Jellyfin.Plugin.IlVod.Providers.Reshet13;

/// <summary>
/// A season's id: its <see cref="Reshet13Episode.SeasonIndex"/> as text, or <see cref="NoSeason"/>
/// for the episodes that have none. Both halves need the one spelling — the transformer hands it to
/// Jellyfin, the source filters a show's episodes by it — so it lives here, beside the records.
/// </summary>
public static class Reshet13SeasonId
{
    /// <summary>Not a number, so it can never collide with a real <c>SeasonNumber</c>.</summary>
    public const string NoSeason = "none";

    public static string Of(int? seasonIndex) =>
        seasonIndex?.ToString(CultureInfo.InvariantCulture) ?? NoSeason;

    /// <exception cref="FormatException"><paramref name="seasonId"/> is neither a whole number nor
    /// <see cref="NoSeason"/>.</exception>
    public static int? Parse(string seasonId)
    {
        if (seasonId == NoSeason)
        {
            return null;
        }

        return int.TryParse(seasonId, NumberStyles.None, CultureInfo.InvariantCulture, out int seasonIndex)
            ? seasonIndex
            : throw new FormatException($"reshet13 season id '{seasonId}' is not a season index");
    }
}
