using System.Globalization;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Hls;

namespace Jellyfin.Plugin.IlVod.Providers.Mako.Transform;

/// <summary>
/// mako's converting half: turns the records <c>MakoSource</c> reads off mako's pages into the
/// shared model. Pure — it sees no network, cache or clock, and nothing of the source but the
/// records the two share (<c>MakoContent.cs</c>).
/// </summary>
public sealed class MakoTransformer : IContentTransformer<MakoShowIndex, ShowPage, MakoSeasonPage, MakoResolvedStream>
{
    private const string HlsContainer = "hls";

    // The two-digit year in extraInfo's air date: 70-99 means 19xx, anything else 20xx.
    private const int TwoDigitYearCenturyPivot = 70;
    private const int NineteenHundred = 1900;
    private const int TwoThousand = 2000;

    private static readonly Regex EpisodeNumberPattern = new(@"פרק\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex AirDatePattern = new(@"@(\d{2})\.(\d{2})\.(\d{2})", RegexOptions.Compiled);

    /// <summary>
    /// One show per distinct page path, in the order the category lists first name it, carrying
    /// every category whose list names it — mako's own names, unmapped. Then every show only the
    /// full list names, in its order, with no genre: the aggregation layer files those under its
    /// fallback category. The full list never changes a listed show's genres or position.
    /// </summary>
    public IReadOnlyList<Show> ToShows(MakoShowIndex index)
    {
        OrderedDictionary<string, Show> showsByPath = new(StringComparer.Ordinal);
        foreach (MakoGenreList genre in index.Genres)
        {
            foreach (IndexEntry entry in genre.Entries)
            {
                AddGenre(showsByPath, entry, genre.GenreName);
            }
        }

        foreach (IndexEntry entry in index.AllShows)
        {
            showsByPath.TryAdd(entry.PagePath, ToShow(entry, genres: []));
        }

        return [.. showsByPath.Values];
    }

    public IReadOnlyList<Season> ToSeasons(ShowPage showPage) =>
        showPage.Seasons.Select(season => new Season(season.PageUrl, season.Number, season.Title)).ToList();

    /// <summary>An entry whose <c>extraInfo</c> states no episode number is numbered by its
    /// position among the season's playable entries.</summary>
    public IReadOnlyList<Episode> ToEpisodes(MakoSeasonPage seasonPage) =>
        seasonPage.Episodes
            .Select((entry, index) => new Episode(
                Id: entry.Key.ToString(),
                ShowName: seasonPage.ShowName,
                SeasonNumber: seasonPage.SeasonNumber,
                Number: EpisodeNumberFrom(entry.ExtraInfo, position: index + 1),
                Name: entry.Title,
                AirDate: AirDateFrom(entry.ExtraInfo),
                Duration: null,
                ThumbnailUrl: entry.ThumbnailUrl))
            .ToList();

    /// <summary>A stream whose master playlist could not be read is described as mako's streams
    /// were measured (<see cref="MakoStreams.DefaultDescription"/>) rather than not at all.</summary>
    public StreamLink ToStreamLink(MakoResolvedStream stream)
    {
        StreamDescription description = stream.Variant is { } variant
            ? new StreamDescription(variant.Width, variant.Height, variant.BitsPerSecond, variant.VideoProfile, variant.VideoLevel)
            {
                Format = MakoStreams.Format,
            }
            : MakoStreams.DefaultDescription;

        return new StreamLink(stream.Url, HlsContainer, description with { Duration = stream.Duration });
    }

    /// <summary>Adds <paramref name="entry"/>'s show under <paramref name="genreName"/>, or adds
    /// that genre to the show already there — in place, so it keeps its first position — unless
    /// the same list named it twice.</summary>
    private static void AddGenre(OrderedDictionary<string, Show> showsByPath, IndexEntry entry, string genreName)
    {
        if (!showsByPath.TryGetValue(entry.PagePath, out Show? known))
        {
            showsByPath.Add(entry.PagePath, ToShow(entry, [genreName]));
        }
        else if (!known.Genres.Contains(genreName, StringComparer.Ordinal))
        {
            showsByPath[entry.PagePath] = known with { Genres = [.. known.Genres, genreName] };
        }
    }

    private static Show ToShow(IndexEntry entry, IReadOnlyList<string> genres) =>
        new(entry.PagePath, entry.Name, entry.PosterUrl, Overview: null, Genres: genres);

    private static int EpisodeNumberFrom(string? extraInfo, int position)
    {
        Match match = extraInfo is null ? Match.Empty : EpisodeNumberPattern.Match(extraInfo);
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : position;
    }

    private static DateTime? AirDateFrom(string? extraInfo)
    {
        Match match = extraInfo is null ? Match.Empty : AirDatePattern.Match(extraInfo);
        if (!match.Success)
        {
            return null;
        }

        int day = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        int month = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        int twoDigitYear = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        int year = (twoDigitYear >= TwoDigitYearCenturyPivot ? NineteenHundred : TwoThousand) + twoDigitYear;

        try
        {
            return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
