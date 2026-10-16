using System.Text.Json;

namespace Jellyfin.Plugin.IlVod.Cache;

/// <summary>
/// A provider's parsed records, kept as JSON in a <see cref="TimedCache"/>. The records are cached
/// rather than the provider's answers because those run far larger — a long show's raw Kaltura
/// episode list is about 1 MB, Kan's lobby 2 MB — of which the records keep a small fraction. A
/// cached copy that no longer reads back as the whole record (written by an older or newer version
/// of the plugin) counts as no copy at all.
/// </summary>
public sealed class RecordCache
{
    /// <summary>
    /// By default System.Text.Json fills a missing constructor parameter with <c>null</c>, so a copy
    /// in another shape would read back with <c>null</c> lists and throw on every open until it
    /// expired. These options refuse a missing parameter, and a <c>null</c> where the record allows
    /// none, so such a copy fails to read instead.
    /// </summary>
    private static readonly JsonSerializerOptions CompleteRecordsOnly = new()
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    private readonly TimedCache _cache;

    public RecordCache(TimedCache cache)
    {
        _cache = cache;
    }

    /// <summary>The copy under <paramref name="key"/> while it is within its lifetime.</summary>
    public T? ReadFresh<T>(string key)
        where T : class
    {
        CacheRead cached = _cache.Read(key);
        return cached.IsFresh ? Deserialize<T>(cached.Value!) : null;
    }

    /// <summary>Whatever copy is under <paramref name="key"/>, fresh or stale.</summary>
    public T? ReadAny<T>(string key)
        where T : class
    {
        CacheRead cached = _cache.Read(key);
        return cached.HasValue ? Deserialize<T>(cached.Value!) : null;
    }

    public void Write<T>(string key, T record, TimeSpan lifetime) =>
        _cache.Write(key, JsonSerializer.Serialize(record, CompleteRecordsOnly), lifetime);

    private static T? Deserialize<T>(string json)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, CompleteRecordsOnly);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
