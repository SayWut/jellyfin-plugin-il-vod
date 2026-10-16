using System.Globalization;
using System.Text.Json;

namespace Jellyfin.Plugin.IlVod.Providers.Kan.Source;

/// <summary>
/// Reads a program feed (<c>api/mobile/program</c>) into a <see cref="KanShowPage"/>. The feed
/// tells an unknown id apart only by naming no program, so that is what makes an answer no feed, as
/// does one with no entry list; a program whose list is empty (a film played from its show page) is
/// a valid, empty show.
/// </summary>
public static class KanFeed
{
    private const string AudioType = "audio";
    private const string StillKey = "image_base_16x9";
    private const string BroadcastDateFormat = "dd-MM-yyyy";

    /// <exception cref="FormatException"><paramref name="json"/> is not a feed naming a program.</exception>
    public static KanShowPage Parse(string json, string programId)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return ReadShowPage(document.RootElement, programId);
        }
        catch (JsonException exception)
        {
            throw new FormatException($"kan program {programId}: the answer is not JSON", exception);
        }
    }

    private static KanShowPage ReadShowPage(JsonElement feed, string programId)
    {
        string showName = Text(feed, "title")?.Trim() ?? string.Empty;
        if (showName.Length == 0)
        {
            throw new FormatException($"kan program {programId}: the answer names no program");
        }

        if (Child(feed, "entry") is not { ValueKind: JsonValueKind.Array } entries)
        {
            throw new FormatException($"kan program {programId}: the answer lists no entries");
        }

        List<JsonElement> videos = [.. entries.EnumerateArray().Where(IsVideo)];
        List<KanEntry> episodes = [.. videos.Where(HasPageId).Select(ReadEntry)];
        if (videos.Count > 0 && episodes.Count == 0)
        {
            throw new FormatException($"kan program {programId}: no entry has an id this plugin can read");
        }

        return new KanShowPage(programId, showName, episodes);
    }

    /// <summary>A YouTube entry is the same episode on Kan's own player too; an audio entry is a radio item.</summary>
    private static bool IsVideo(JsonElement entry) => Text(Child(entry, "type"), "value") != AudioType;

    /// <summary>One with no numeric id has no page to play. A few such entries are skipped; a feed of
    /// nothing else means Kan changed its ids, and is refused rather than read as an empty show,
    /// which Jellyfin would answer by deleting every episode.</summary>
    private static bool HasPageId(JsonElement entry) => Text(entry, "id") is string id && KanIds.IsNumeric(id);

    private static KanEntry ReadEntry(JsonElement entry)
    {
        JsonElement? extensions = Child(entry, "extensions");
        JsonElement? analytics = Child(extensions, "analyticsCustomProperties");

        return new KanEntry(
            EntryId: Text(entry, "id")!,
            Title: Text(entry, "title")?.Trim() ?? string.Empty,
            Season: PositiveNumber(analytics, "season"),
            EpisodeNumber: PositiveNumber(analytics, "episode_number"),
            BroadcastDate: BroadcastDate(Text(analytics, "content_customDimension_7")),
            Duration: KanDuration.Parse(Text(analytics, "duration")) ?? KanDuration.Parse(Text(extensions, "duration_length")),
            ThumbnailUrl: StillOf(entry));
    }

    /// <summary>Never the entry's <c>published</c>: for older episodes that is the date they were imported.</summary>
    private static DateTime? BroadcastDate(string? text) =>
        DateTime.TryParseExact(text?.Trim(), BroadcastDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date)
            ? date
            : null;

    private static string? StillOf(JsonElement entry) =>
        Items(Child(entry, "media_group"))
            .SelectMany(group => Items(Child(group, "media_item")))
            .Where(item => Text(item, "key") == StillKey)
            .Select(item => Text(item, "src"))
            .FirstOrDefault(src => !string.IsNullOrEmpty(src));

    /// <summary>0 is how the feed says "none" for a season and an episode number alike.</summary>
    private static int? PositiveNumber(JsonElement? element, string name) =>
        Child(element, name) switch
        {
            { ValueKind: JsonValueKind.Number } number when number.TryGetInt32(out int value) && value > 0 => value,
            { ValueKind: JsonValueKind.String } text when int.TryParse(text.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value > 0 => value,
            _ => null,
        };

    private static JsonElement? Child(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } parent && parent.TryGetProperty(name, out JsonElement child) ? child : null;

    private static string? Text(JsonElement? element, string name) =>
        Child(element, name) is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;

    private static IEnumerable<JsonElement> Items(JsonElement? element) =>
        element is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray() : [];
}
