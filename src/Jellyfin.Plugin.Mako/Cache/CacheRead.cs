namespace Jellyfin.Plugin.Mako.Cache;

/// <summary>
/// The result of reading a <see cref="TimedCache"/>: the value, if one was ever written, and
/// whether it is still within its lifetime.
/// </summary>
public readonly record struct CacheRead(string? Value, bool IsFresh)
{
    /// <summary>
    /// True when a value exists at all, fresh or stale. A stale value — <see cref="HasValue"/>
    /// true, <see cref="IsFresh"/> false — is what callers serve while a bot challenge is
    /// backing off.
    /// </summary>
    public bool HasValue => Value is not null;
}
