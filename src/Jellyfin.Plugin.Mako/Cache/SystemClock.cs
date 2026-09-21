namespace Jellyfin.Plugin.Mako.Cache;

/// <summary>
/// The real clock: the system's current UTC time.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
