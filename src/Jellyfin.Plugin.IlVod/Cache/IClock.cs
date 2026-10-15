namespace Jellyfin.Plugin.IlVod.Cache;

/// <summary>
/// The current time, as a seam so callers that depend on it can be tested with a fake.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
