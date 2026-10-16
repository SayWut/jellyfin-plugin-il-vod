using System.Globalization;
using System.Text.Json;

namespace Jellyfin.Plugin.IlVod.Providers.Kan.Source;

/// <summary>
/// Reads a program feed (<c>api/mobile/program</c>) into a <see cref="KanShowPage"/>. The feed
/// tells an unknown id apart only by naming no program, so that is what makes an answer no feed;
/// a program with no entries (a film not yet online) is a valid, empty show.
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

        IEnumerable<JsonElement> entries = Child(feed, "entry") is { ValueKind: JsonValueKind.Array } array
            ? array.EnumerateArray()
            : [];
        return new KanShowPage(programId, showName, [.. entries.Where(IsPlayable).Select(ReadEntry)]);
    }

    /// <summary>A YouTube entry is the same episode on Kan's own player too; an audio entry is a
    /// radio item, and one with no numeric id has no page to play.</summary>
    private static bool IsPlayable(JsonElement entry) =>
        Text(Child(entry, "type"), "value") != AudioType
        && Text(entry, "id") is string id
        && KanEpisodeId.IsNumericId(id);

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
