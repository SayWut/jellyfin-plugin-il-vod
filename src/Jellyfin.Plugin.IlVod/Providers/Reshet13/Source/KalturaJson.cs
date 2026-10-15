using System.Globalization;
using System.Text.Json;

namespace Jellyfin.Plugin.IlVod.Providers.Reshet13.Source;

/// <summary>
/// Guarded reads of Kaltura's JSON, shared by the envelope (<see cref="KalturaOttClient"/>) and the
/// assets (<see cref="KalturaAssets"/>): a missing or wrongly typed property degrades to
/// <c>null</c> or an empty sequence instead of throwing, the way <c>MakoPages</c> reads mako's.
/// </summary>
internal static class KalturaJson
{
    public static JsonElement? Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value)
            ? value
            : null;

    public static JsonElement? Object(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.Object } value ? value : null;

    public static string? String(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    /// <summary>The array's items; none when the property is missing or not an array.</summary>
    public static IEnumerable<JsonElement> Items(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.Array } value ? value.EnumerateArray() : [];

    /// <summary>A whole number Kaltura sends as a JSON number (<c>4</c>, or a double <c>4.0</c>)
    /// or as digits in a string.</summary>
    public static long? WholeNumber(JsonElement? value) => value switch
    {
        { ValueKind: JsonValueKind.Number } number => WholeNumberOf(number),
        { ValueKind: JsonValueKind.String } text => WholeNumberOf(text.GetString()),
        _ => null,
    };

    private static long? WholeNumberOf(JsonElement number)
    {
        if (number.TryGetInt64(out long whole))
        {
            return whole;
        }

        return number.TryGetDouble(out double real) && real == Math.Floor(real) && Math.Abs(real) <= long.MaxValue
            ? (long)real
            : null;
    }

    private static long? WholeNumberOf(string? digits) =>
        long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed) ? parsed : null;
}
