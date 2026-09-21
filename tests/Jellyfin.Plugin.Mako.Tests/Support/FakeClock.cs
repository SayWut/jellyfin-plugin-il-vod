using Jellyfin.Plugin.Mako.Cache;

namespace Jellyfin.Plugin.Mako.Tests.Support;

/// <summary>
/// A clock a test can move forward on demand, so cache lifetimes can be tested without waiting
/// on the real one.
/// </summary>
public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;

    public void Advance(TimeSpan by) => UtcNow += by;
}
