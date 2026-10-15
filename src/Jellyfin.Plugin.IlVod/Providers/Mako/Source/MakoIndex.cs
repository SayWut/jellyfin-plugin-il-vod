using System.Text.Json;

namespace Jellyfin.Plugin.IlVod.Providers.Mako.Source;

/// <summary>
/// Parsing only, for mako's show index: the genre menu (<c>menuItems[]</c>), whose category lists
/// <see cref="ParseGenres"/> keeps (<see cref="MakoIndexPaths.IsCategoryList"/>), and a show list —
/// a category's or the full index (<c>items[]</c>, one shape for both). Tolerant like <c>MakoPages</c>: a missing
/// or wrongly-typed property degrades to a skipped entry or null, never a throw; only text that
/// is not a JSON object at all is a <see cref="FormatException"/>.
/// </summary>
public static class MakoIndex
{
    private const string MenuItemsProperty = "menuItems";
    private const string ItemsProperty = "items";
    private const string TitleProperty = "title";
    private const string UrlProperty = "url";
    private const string PageUrlProperty = "pageUrl";
    private const string PictureProperty = "pic";

    private const string NotJsonMessage = "mako index page is not valid JSON";
    private const string NotObjectMessage = "mako index page is not a JSON object";

    public static IReadOnlyList<Genre> ParseGenres(string menuJson)
    {
        using JsonDocument document = ParseObjectOrThrow(menuJson);
        return ObjectsIn(document.RootElement, MenuItemsProperty)
            .Select(TryReadGenre)
            .OfType<Genre>()
            .ToList();
    }

    public static IReadOnlyList<IndexEntry> ParseEntries(string listJson)
    {
        using JsonDocument document = ParseObjectOrThrow(listJson);
        return ObjectsIn(document.RootElement, ItemsProperty)
            .Select(TryReadEntry)
            .OfType<IndexEntry>()
            .ToList();
    }

    private static Genre? TryReadGenre(JsonElement item)
    {
        string? name = TryGetString(item, TitleProperty);
        string? pagePath = TryGetString(item, UrlProperty);
        return !string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(pagePath) && MakoIndexPaths.IsCategoryList(name, pagePath)
            ? new Genre(name, pagePath)
            : null;
    }

    private static IndexEntry? TryReadEntry(JsonElement item)
    {
        string? pagePath = TryGetString(item, PageUrlProperty);
        string? name = TryGetString(item, TitleProperty);
        return !string.IsNullOrEmpty(pagePath) && !string.IsNullOrEmpty(name)
            ? new IndexEntry(pagePath, name, TryGetString(item, PictureProperty))
            : null;
    }

    private static JsonDocument ParseObjectOrThrow(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new FormatException(NotJsonMessage);
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw new FormatException(NotObjectMessage);
        }

        return document;
    }

    private static IEnumerable<JsonElement> ObjectsIn(JsonElement root, string arrayName) =>
        root.TryGetProperty(arrayName, out JsonElement array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Where(element => element.ValueKind == JsonValueKind.Object)
            : [];

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
