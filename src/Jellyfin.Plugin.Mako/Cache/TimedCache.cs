using System.Text.Json;

namespace Jellyfin.Plugin.Mako.Cache;

/// <summary>
/// A string-keyed, string-valued cache whose entries expire after a caller-chosen lifetime but
/// remain readable, marked stale, after they do. Optionally persisted to a JSON file so a
/// process restart does not lose entries; without a path it lives in memory only.
/// </summary>
public sealed class TimedCache
{
    private readonly IClock _clock;
    private readonly string? _persistencePath;
    private readonly object _lock = new();
    private readonly Dictionary<string, CacheEntry> _entries;

    public TimedCache(IClock clock, string? persistencePath = null)
    {
        _clock = clock;
        _persistencePath = persistencePath;
        _entries = ReadEntriesFromDisk(persistencePath);
    }

    public CacheRead Read(string key)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(key, out CacheEntry entry))
            {
                return new CacheRead(null, false);
            }

            return new CacheRead(entry.Value, IsFresh(entry));
        }
    }

    public void Write(string key, string value, TimeSpan lifetime)
    {
        lock (_lock)
        {
            _entries[key] = new CacheEntry(value, _clock.UtcNow + lifetime);
            WriteEntriesToDisk();
        }
    }

    private bool IsFresh(CacheEntry entry) => _clock.UtcNow < entry.ExpiresAt;

    private static Dictionary<string, CacheEntry> ReadEntriesFromDisk(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return [];
        }

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(json) ?? [];
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void WriteEntriesToDisk()
    {
        if (_persistencePath is null)
        {
            return;
        }

        string temporaryPath = $"{_persistencePath}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_entries));
            File.Move(temporaryPath, _persistencePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An unwritable cache path must never take the caller down with it; the value just
            // written is still there in memory for this process's lifetime.
            DeleteIfExists(temporaryPath);
        }
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best-effort tidy-up only: the same unwritable location that failed the write is
            // free to fail this too.
        }
    }

    private readonly record struct CacheEntry(string Value, DateTimeOffset ExpiresAt);
}
