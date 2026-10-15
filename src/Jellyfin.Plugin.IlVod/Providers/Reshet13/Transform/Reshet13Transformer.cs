using System.Globalization;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Hls;

namespace Jellyfin.Plugin.IlVod.Providers.Reshet13.Transform;

/// <summary>
/// Reshet 13's converting half: turns the records <c>Reshet13Source</c> reads off Kaltura into the
/// shared model. Pure — it sees no network, cache or clock, and nothing of the source but the
/// records the two share (<c>Reshet13Content.cs</c>).
/// </summary>
/// <remarks>
/// Kaltura's <c>SeasonNumber</c> and <c>EpisodeNumber</c> are sort indices, not the numbers a viewer
/// sees, and their direction differs between shows (in האח הגדול index 1 is "עונה 8"). So seasons
/// and episodes are numbered from their names first and fall back to the indices only when the
/// name states no number.
/// </remarks>
public sealed class Reshet13Transformer : IContentTransformer<Reshet13ShowIndex, Reshet13ShowPage, Reshet13SeasonPage, Reshet13Stream>
{
    private const string HlsContainer = "hls";

    /// <summary>The season the episodes with no <c>SeasonNumber</c> are gathered under; numbered 0
    /// so it lists first, the way Jellyfin lists specials.</summary>
    private const string ExtrasSeasonName = "פרקים נוספים";
    private const int ExtrasSeasonNumber = 0;

    // "עונה 8", "VIP עונה 1" — but not "העונה החמה • קיץ 2021", whose only number is a year.
    private static readonly Regex SeasonNumberPattern = new(@"עונה\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex EpisodeNumberPattern = new(@"פרק\s*(\d+)", RegexOptions.Compiled);

    /// <summary>One show per series, in the order Kaltura lists them, carrying Reshet 13's own genre
    /// names, unmapped.</summary>
    public IReadOnlyList<Show> ToShows(Reshet13ShowIndex index) =>
        index.Series.Select(ToShow).ToList();

    /// <summary>One season per distinct <c>SeasonNumber</c> among the show's episodes — Kaltura's
    /// season assets are incomplete, so they only lend their names.</summary>
    public IReadOnlyList<Season> ToSeasons(Reshet13ShowPage showPage) =>
        showPage.Episodes
            .GroupBy(episode => episode.SeasonIndex)
            .Select(season => ToSeason(season.Key, showPage.SeasonAssetNameOf(season.Key), season.ToList()))
            .OrderBy(season => season.Number)
            .ThenBy(season => season.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>Episodes are filed under the very number <see cref="ToSeasons"/> lists their season
    /// under, and carry no air date: Kaltura's <c>startDate</c> is the date of a bulk import.</summary>
    public IReadOnlyList<Episode> ToEpisodes(Reshet13SeasonPage seasonPage)
    {
        int seasonNumber = ToSeason(seasonPage.SeasonIndex, seasonPage.SeasonAssetName, seasonPage.Episodes).Number;

        return seasonPage.Episodes
            .Select((episode, index) => ToEpisode(episode, seasonPage.ShowName, seasonNumber, position: index + 1))
            .OrderBy(episode => episode.Number)
            .ThenBy(episode => episode.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>A stream whose master playlist could not be read is still described in Reshet 13's
    /// measured format, with nothing claimed about its resolution.</summary>
    public StreamLink ToStreamLink(Reshet13Stream stream)
    {
        StreamDescription description = stream.Variant is { } variant
            ? new StreamDescription(variant.Width, variant.Height, variant.BitsPerSecond, variant.VideoProfile, variant.VideoLevel)
            : new StreamDescription(null, null, null, MasterPlaylist.UnmeasuredProfile, null);

        return new StreamLink(stream.ManifestUrl, HlsContainer, description with { Format = Reshet13Streams.Format, Duration = stream.Duration });
    }

    private static Show ToShow(Reshet13Series series) =>
        new(series.SeriesId, series.Name, KalturaImages.PosterUrl(series.Images), series.Overview, series.Genres);

    /// <summary>The one place a season gets its name and number, so the season list and the
    /// episodes filed under it can never disagree.</summary>
    private static Season ToSeason(int? seasonIndex, string? seasonAssetName, IReadOnlyList<Reshet13Episode> episodes)
    {
        string name = SeasonNameOf(seasonIndex, seasonAssetName, episodes);
        return new Season(Reshet13SeasonId.Of(seasonIndex), SeasonNumberOf(name, seasonIndex), name);
    }

    /// <summary>The season asset's name; else the <c>SeasonName</c> tag its episodes carry most
    /// (they can disagree within one index); else a name made from the index.</summary>
    private static string SeasonNameOf(int? seasonIndex, string? seasonAssetName, IReadOnlyList<Reshet13Episode> episodes)
    {
        if (seasonIndex is not { } index)
        {
            return ExtrasSeasonName;
        }

        return seasonAssetName
            ?? MostCommonSeasonNameTag(episodes)
            ?? string.Create(CultureInfo.InvariantCulture, $"עונה {index}");
    }

    /// <returns>The commonest tag, the first seen on a tie; <c>null</c> when no episode has one.</returns>
    private static string? MostCommonSeasonNameTag(IReadOnlyList<Reshet13Episode> episodes) =>
        episodes
            .Select(episode => episode.SeasonName)
            .OfType<string>()
            .GroupBy(seasonName => seasonName, StringComparer.Ordinal)
            .OrderByDescending(sameName => sameName.Count())
            .Select(sameName => sameName.Key)
            .FirstOrDefault();

    private static int SeasonNumberOf(string seasonName, int? seasonIndex)
    {
        Match match = SeasonNumberPattern.Match(seasonName);
        return match.Success ? ParseNumber(match) : seasonIndex ?? ExtrasSeasonNumber;
    }

    private static Episode ToEpisode(Reshet13Episode episode, string showName, int seasonNumber, int position) =>
        new(
            Id: episode.AssetId,
            ShowName: showName,
            SeasonNumber: seasonNumber,
            Number: EpisodeNumberOf(episode, position),
            Name: episode.Name,
            AirDate: null,
            Duration: episode.Duration,
            ThumbnailUrl: KalturaImages.ThumbnailUrl(episode.Images));

    /// <summary>"פרק N" in the name; else the episode's index; else its position in the season.</summary>
    private static int EpisodeNumberOf(Reshet13Episode episode, int position)
    {
        Match match = EpisodeNumberPattern.Match(episode.Name);
        return match.Success ? ParseNumber(match) : episode.EpisodeIndex ?? position;
    }

    private static int ParseNumber(Match match) =>
        int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
}
