using Jellyfin.Plugin.IlVod.Core;

namespace Jellyfin.Plugin.IlVod.Providers.Reshet13.Source;

/// <summary>
/// Reshet 13's catalogue could not answer right now: a refused or failed request, a timeout, or a
/// Kaltura error that logging in again did not cure. The message names what was asked and why it
/// failed, but never a session token.
/// </summary>
public sealed class Reshet13UnavailableException : ProviderUnavailableException
{
    public Reshet13UnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
