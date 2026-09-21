using System.Security.Cryptography;

namespace Jellyfin.Plugin.Mako.MakoApi;

/// <summary>
/// The one per-install device id mako's token request needs (field <c>du</c>): generated once as
/// <c>"A1"</c> plus 23 random lowercase hex characters, matching the shape of the Android app's
/// own padded <c>ANDROID_ID</c>, and persisted to a small file so it survives a restart. Reading
/// or writing that file is best-effort only — playback must not fail because a device id could
/// not be saved, so any I/O failure yields a freshly generated id that simply lives for the rest
/// of this process instead of throwing out of <see cref="Get"/>.
/// </summary>
public sealed class DeviceIdentity
{
    private const string Prefix = "A1";
    private const int RandomHexCharCount = 23;

    private readonly string _filePath;
    private readonly object _lock = new();
    private string? _cachedId;

    public DeviceIdentity(string filePath)
    {
        _filePath = filePath;
    }

    public string Get()
    {
        lock (_lock)
        {
            return _cachedId ??= ReadPersistedId() ?? CreateAndPersistId();
        }
    }

    private string? ReadPersistedId()
    {
        try
        {
            return File.Exists(_filePath) ? NonEmptyOrNull(File.ReadAllText(_filePath)) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? NonEmptyOrNull(string text)
    {
        string trimmed = text.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private string CreateAndPersistId()
    {
        string id = Prefix + RandomNumberGenerator.GetHexString(RandomHexCharCount, lowercase: true);
        TryPersist(id);
        return id;
    }

    private void TryPersist(string id)
    {
        try
        {
            File.WriteAllText(_filePath, id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Kept in memory for the rest of this process's lifetime by Get()'s cache — playback
            // must not fail just because the id could not be written to disk.
        }
    }
}
