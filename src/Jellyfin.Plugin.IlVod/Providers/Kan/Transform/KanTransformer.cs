using System.Globalization;
using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Hls;

namespace Jellyfin.Plugin.IlVod.Providers.Kan.Transform;

/// <summary>
/// Kan 11's converting half: turns the records <c>Source/</c> reads off kan.org.il into the shared
/// model. Pure — it sees no network, cache or clock, and nothing of the source but the records the
/// two share (<c>KanContent.cs</c>).
/// </summary>
public sealed class KanTransformer : IContentTransformer<KanShowIndex, KanShowPage, KanSeasonPage, KanStream>
{
    private const string HlsContainer = "hls";

    /// <summary>Beside numbered seasons, the episodes stating none are trailers and clips: numbered 0
    /// so they list first, the way Jellyfin lists specials.</summary>
    private const string ExtrasSeasonName = "פרקים נוספים";
    private const int ExtrasSeasonNumber = 0;

    /// <summary>In a show with no numbered seasons (news, a single film) they are all there is.</summary>
    private const string OnlySeasonName = "פרקים";
    private const int OnlySeasonNumber = 1;

    /// <summary>One show per card, in the order the genre pages listed them, carrying Kan's own
    /// genre names, unmapped.</summary>
    public IReadOnlyList<Show> ToShows(KanShowIndex index) =>
        index.Shows.Select(ToShow).ToList();

    /// <summary>One season per distinct season the show's episodes state.</summary>
    public IReadOnlyList<Season> ToSeasons(KanShowPage showPage)
    {
        bool hasNumberedSeasons = showPage.HasNumberedSeasons();

        return showPage.Episodes
            .Select(episode => episode.Season)
            .Distinct()
            .Select(season => ToSeason(season, hasNumberedSeasons))
            .OrderBy(season => season.Number)
            .ToList();
    }

    /// <summary>Episodes are filed under the very number <see cref="ToSeasons"/> lists their season under.</summary>
    public IReadOnlyList<Episode> ToEpisodes(KanSeasonPage seasonPage)
    {
        int seasonNumber = ToSeason(seasonPage.Season, seasonPage.ShowHasNumberedSeasons).Number;
        int count = seasonPage.Episodes.Count;

        return seasonPage.Episodes
            .Select((entry, index) => ToEpisode(entry, seasonPage, seasonNumber, positionFromOldest: count - index))
            .OrderBy(episode => episode.Number)
            .ThenBy(episode => episode.AirDate)
            .ThenBy(episode => episode.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The URL stays the master playlist: Kan's audio is a separate rendition, so a variant's
    /// own URL would play silent. ffmpeg is told instead which of the master's tracks to map.</summary>
    public StreamLink ToStreamLink(KanStream stream)
    {
        StreamDescription description = DescribedFormat(stream) with { Format = KanStreams.Format, Duration = stream.Duration };

        if (stream.StreamIndices is { } indices)
        {
            description = description with { VideoStreamIndex = indices.Video, AudioStreamIndex = indices.Audio };
        }

        return new StreamLink(stream.Url, HlsContainer, description);
    }

    private static Show ToShow(KanShowCard card) =>
        new(card.ProgramId, card.Name, KanImages.PosterUrl(card), card.Overview, card.Genres);

    /// <summary>The one place a season gets its name and number, so the season list and the
    /// episodes filed under it can never disagree. Its id is the feed's own season, never a
    /// position, so it stays stable across restarts.</summary>
    private static Season ToSeason(int? season, bool showHasNumberedSeasons)
    {
        string id = KanSeasonId.Of(season);
        if (season is { } number)
        {
            return new Season(id, number, string.Create(CultureInfo.InvariantCulture, $"עונה {number}"));
        }

        return showHasNumberedSeasons
            ? new Season(id, ExtrasSeasonNumber, ExtrasSeasonName)
            : new Season(id, OnlySeasonNumber, OnlySeasonName);
    }

    /// <param name="positionFromOldest">The episode's number when the feed states none: the feed
    /// lists newest first, so it is counted from the end.</param>
    private static Episode ToEpisode(KanEntry entry, KanSeasonPage seasonPage, int seasonNumber, int positionFromOldest) =>
        new(
            Id: KanEpisodeId.Of(seasonPage.ProgramId, entry.EntryId),
            ShowName: seasonPage.ShowName,
            SeasonNumber: seasonNumber,
            Number: entry.EpisodeNumber ?? positionFromOldest,
            Name: entry.Title,
            AirDate: entry.BroadcastDate,
            Duration: entry.Duration,
            ThumbnailUrl: entry.ThumbnailUrl);

    /// <summary>A stream whose master playlist could not be read claims nothing about its resolution.</summary>
    private static StreamDescription DescribedFormat(KanStream stream) =>
        stream.Variant is { } variant
            ? new StreamDescription(variant.Width, variant.Height, variant.BitsPerSecond, variant.VideoProfile, variant.VideoLevel)
            : new StreamDescription(null, null, null, MasterPlaylist.UnmeasuredProfile, null);
}
