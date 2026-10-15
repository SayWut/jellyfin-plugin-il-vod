using System.Globalization;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.IlVod.Core;

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
    /// One show per distinct page path, in the order mako first lists it, carrying every genre
    /// whose list names it — mako's own genre names, unmapped.
    /// </summary>
    public IReadOnlyList<Show> ToShows(MakoShowIndex index)
    {
        Dictionary<string, Show> showsByPath = new(StringComparer.Ordinal);
        List<string> order = [];
        foreach (MakoGenreList genre in index.Genres)
        {
            foreach (IndexEntry entry in genre.Entries)
            {
                if (showsByPath.TryGetValue(entry.PagePath, out Show? known))
                {
                    if (!known.Genres.Contains(genre.GenreName, StringComparer.Ordinal))
                    {
                        showsByPath[entry.PagePath] = known with { Genres = [.. known.Genres, genre.GenreName] };
                    }

                    continue;
                }

                showsByPath[entry.PagePath] = new Show(entry.PagePath, entry.Name, entry.PosterUrl, Overview: null, Genres: [genre.GenreName]);
                order.Add(entry.PagePath);
            }
        }

        return order.Select(path => showsByPath[path]).ToList();
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
