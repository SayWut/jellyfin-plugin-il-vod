using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.IlVod.Providers.Mako.Source;

/// <summary>
/// The one per-install device id mako's token request needs (field <c>du</c>): generated once as
/// <c>"A1"</c> plus 23 random lowercase hex characters, matching the shape of the Android app's
/// own padded <c>ANDROID_ID</c>, and persisted to a small file (written atomically, like
/// <c>TimedCache</c>) so it survives a restart. A file that cannot be read, does not exist, or
/// holds anything other than a well-formed id — corrupted, half-written, or left over from some
/// earlier, differently-shaped version — is treated exactly like a missing file: a fresh id is
/// generated and overwrites it, rather than mako being sent a garbled <c>du</c> forever. Reading
/// or writing is best-effort only — playback must not fail because a device id could not be
/// saved, so any I/O failure yields a freshly generated id that simply lives for the rest of this
/// process instead of throwing out of <see cref="Get"/>.
/// </summary>
public sealed class DeviceIdentity
{
    private const string Prefix = "A1";
    private const int RandomHexCharCount = 23;

    /// <summary>
    /// The one place that decides whether text is a well-formed id, built from
    /// <see cref="Prefix"/> and <see cref="RandomHexCharCount"/> so the shape it validates can
    /// never drift from the shape <see cref="CreateAndPersistId"/> generates.
    /// </summary>
    private static readonly Regex ValidIdPattern =
        new($"^{Regex.Escape(Prefix)}[0-9a-f]{{{RandomHexCharCount}}}$", RegexOptions.Compiled);

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
            return _cachedId ??= ReadValidPersistedId() ?? CreateAndPersistId();
        }
    }

    private string? ReadValidPersistedId()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            string trimmed = File.ReadAllText(_filePath).Trim();
            return ValidIdPattern.IsMatch(trimmed) ? trimmed : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string CreateAndPersistId()
    {
        string id = Prefix + RandomNumberGenerator.GetHexString(RandomHexCharCount, lowercase: true);
        TryPersist(id);
        return id;
    }

    /// <summary>
    /// Writes to a temporary file and renames it over <see cref="_filePath"/> — the same
    /// write-then-rename shape as <c>TimedCache</c> — so a reader can never observe a
    /// half-written id. Best-effort only: an unwritable location never throws, since the id is
    /// kept in memory for the rest of this process by <see cref="Get"/>'s cache either way.
    /// </summary>
    private void TryPersist(string id)
    {
        string temporaryPath = $"{_filePath}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, id);
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
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
}
