using Jellyfin.Plugin.IlVod.Core;

namespace Jellyfin.Plugin.IlVod.Providers.Mako;

/// <summary>
/// A request to mako failed at the HTTP boundary: a non-success status, a transport failure or a
/// timeout. The message is built by the caller (<see cref="MakoHttpClient"/>) and, per N3, never
/// carries a query string.
/// </summary>
public sealed class MakoRequestException : ProviderUnavailableException
{
    public MakoRequestException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
