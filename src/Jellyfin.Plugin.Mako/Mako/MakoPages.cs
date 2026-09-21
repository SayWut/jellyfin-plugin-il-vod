using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Mako.Model;

namespace Jellyfin.Plugin.Mako.MakoApi;

/// <summary>
/// Parses the JSON mako serves for a show or season page (both have the same shape, spec §5.2):
/// the show's seasons and overview, one season's episodes, and whether a page is the current
/// season. Parsing only - fetching and caching the page text belongs to <c>MakoPageSource</c>.
/// </summary>
public static class MakoPages
{
    private const string EpisodesTabId = "allEpisodes";
    private const string AdComponentLayout = "ad";
    private const string DefaultSeasonName = "עונה 1";

    // The two-digit year in extraInfo's air date: 70-99 means 19xx, anything else 20xx.
    private const int TwoDigitYearCenturyPivot = 70;
    private const int NineteenHundred = 1900;
    private const int TwoThousand = 2000;

    private static readonly Regex SeasonNumberPattern = new(@"\d+", RegexOptions.Compiled);
    private static readonly Regex EpisodeNumberPattern = new(@"פרק\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex AirDatePattern = new(@"@(\d{2})\.(\d{2})\.(\d{2})", RegexOptions.Compiled);

    public static ShowPage ParseShowPage(string pageJson)
    {
        using JsonDocument document = ParseDocument(pageJson);
        JsonElement root = RequireObjectRoot(document.RootElement);

        return new ShowPage(
            PagePath: PageUrlOf(root),
            Overview: TryGetNestedString(root, "hero", "description"),
            PosterUrl: PosterUrlOf(root),
            Seasons: ParseSeasons(root),
            DisplayedSeasonId: DisplayedSeasonIdOf(root));
    }

    public static IReadOnlyList<Episode> ParseEpisodes(string pageJson, string showName, int seasonNumber)
    {
        using JsonDocument document = ParseDocument(pageJson);
        JsonElement root = RequireObjectRoot(document.RootElement);
        JsonElement? episodeEntries = FindEpisodesTab(root);
        if (episodeEntries is null)
        {
            return [];
        }

        List<Episode> episodes = [];
        foreach (JsonElement entry in episodeEntries.Value.EnumerateArray())
        {
            if (IsAdEntry(entry) || TryReadEpisodeKey(entry) is not EpisodeKey key)
            {
                continue;
            }

            episodes.Add(ToEpisode(entry, key, showName, seasonNumber, position: episodes.Count + 1));
        }

        return episodes;
    }

    /// <summary>
    /// True when <paramref name="seasonPagePath"/> names the HIGHEST-numbered season in this
    /// page's own <c>seasons[]</c> — the season actually airing, as opposed to whichever season
    /// the page happens to be showing (<see cref="ShowPage.DisplayedSeasonId"/>): mako marks a
    /// past season's own page as its "current" (displayed) season too, so that flag alone cannot
    /// tell latest from past. No <c>seasons</c> array at all → true (a single-season show has
    /// nothing to be behind).
    /// </summary>
    public static bool IsLatestSeason(string pageJson, string seasonPagePath)
    {
        using JsonDocument document = ParseDocument(pageJson);
        JsonElement root = RequireObjectRoot(document.RootElement);

        if (!TryGetArray(root, "seasons", out _))
        {
            return true;
        }

        Season? latestSeason = ParseSeasons(root).MaxBy(season => season.Number);
        return latestSeason?.Id == seasonPagePath;
    }

    private static JsonDocument ParseDocument(string pageJson)
    {
        try
        {
            return JsonDocument.Parse(pageJson);
        }
        catch (JsonException exception)
        {
            throw new FormatException("mako page is not valid JSON", exception);
        }
    }

    /// <exception cref="FormatException"><paramref name="root"/> is not a JSON object.</exception>
    private static JsonElement RequireObjectRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("mako page is not a JSON object");
        }

        return root;
    }

    private static IReadOnlyList<Season> ParseSeasons(JsonElement root)
    {
        if (!TryGetArray(root, "seasons", out JsonElement seasonEntries))
        {
            return [new Season(PageUrlOf(root) ?? string.Empty, 1, DefaultSeasonName)];
        }

        List<Season> seasons = [];
        foreach (JsonElement entry in seasonEntries.EnumerateArray())
        {
            // mako interleaves non-season placeholders elsewhere on its pages; tolerate the
            // same here rather than assume every array entry is a season object.
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? title = TryGetString(entry, "seasonTitle");
            string id = TryGetString(entry, "pageUrl") ?? string.Empty;
            int number = SeasonNumberFrom(title, position: seasons.Count + 1);
            seasons.Add(new Season(id, number, title ?? string.Empty));
        }

        return seasons;
    }

    private static string? DisplayedSeasonIdOf(JsonElement root)
    {
        if (!TryGetArray(root, "seasons", out JsonElement seasons))
        {
            return PageUrlOf(root);
        }

        foreach (JsonElement entry in seasons.EnumerateArray())
        {
            if (TryGetBoolean(entry, "current"))
            {
                return TryGetString(entry, "pageUrl");
            }
        }

        return null;
    }

    private static int SeasonNumberFrom(string? seasonTitle, int position)
    {
        Match match = seasonTitle is null ? Match.Empty : SeasonNumberPattern.Match(seasonTitle);
        return match.Success ? int.Parse(match.Value, CultureInfo.InvariantCulture) : position;
    }

    private static JsonElement? FindEpisodesTab(JsonElement root)
    {
        if (!TryGetArray(root, "menu", out JsonElement tabs))
        {
            return null;
        }

        JsonElement? firstTabWithEpisodes = null;
        foreach (JsonElement tab in tabs.EnumerateArray())
        {
            if (!TryGetArray(tab, "vods", out JsonElement episodeEntries))
            {
                continue;
            }

            if (TryGetString(tab, "id") == EpisodesTabId)
            {
                return episodeEntries;
            }

            firstTabWithEpisodes ??= episodeEntries;
        }

        return firstTabWithEpisodes;
    }

    private static bool IsAdEntry(JsonElement entry) => TryGetString(entry, "componentLayout") == AdComponentLayout;

    private static EpisodeKey? TryReadEpisodeKey(JsonElement entry)
    {
        string? vcmId = TryGetString(entry, "itemVcmId");
        string? channelId = TryGetString(entry, "channelId");
        string? galleryChannelId = TryGetString(entry, "galleryChannelId");
        return string.IsNullOrEmpty(vcmId) || string.IsNullOrEmpty(channelId) || string.IsNullOrEmpty(galleryChannelId)
            ? null
            : new EpisodeKey(vcmId, channelId, galleryChannelId);
    }

    private static Episode ToEpisode(JsonElement entry, EpisodeKey key, string showName, int seasonNumber, int position)
    {
        string? extraInfo = TryGetString(entry, "extraInfo");
        return new Episode(
            Id: key.ToString(),
            ShowName: showName,
            SeasonNumber: seasonNumber,
            Number: EpisodeNumberFrom(extraInfo, position),
            Name: TryGetString(entry, "title") ?? string.Empty,
            AirDate: AirDateFrom(extraInfo),
            Duration: null,
            ThumbnailUrl: FirstStringIn(entry, "pics", "picUrl"));
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

    private static string? PosterUrlOf(JsonElement root) =>
        TryGetObject(root, "hero", out JsonElement hero) ? FirstStringIn(hero, "pics", "picUrl") : null;

    private static string? FirstStringIn(JsonElement parent, string arrayName, string propertyName)
    {
        if (!TryGetArray(parent, arrayName, out JsonElement array) || array.GetArrayLength() == 0)
        {
            return null;
        }

        return TryGetString(array[0], propertyName);
    }

    private static string? PageUrlOf(JsonElement root) => TryGetString(root, "pageUrl");

    private static string? TryGetNestedString(JsonElement root, string parentName, string propertyName) =>
        TryGetObject(root, parentName, out JsonElement parent) ? TryGetString(parent, propertyName) : null;

    // Every read of a mako page property goes through one of the four guarded accessors below:
    // a wrongly-typed or missing property degrades to null/false/empty instead of throwing
    // (JsonElement.TryGetProperty throws InvalidOperationException on a non-object element).
    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryGetBoolean(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out JsonElement value)
        && value.ValueKind == JsonValueKind.True;

    private static bool TryGetObject(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out value)
            && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetArray(JsonElement element, string propertyName, out JsonElement array)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out array)
            && array.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        array = default;
        return false;
    }
}
