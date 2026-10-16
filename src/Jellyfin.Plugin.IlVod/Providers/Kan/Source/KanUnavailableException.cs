using Jellyfin.Plugin.IlVod.Core;

namespace Jellyfin.Plugin.IlVod.Providers.Kan.Source;

/// <summary>
/// Kan could not answer right now: a refused or failed request, a timeout, or a Cloudflare
/// challenge. The message names what was asked and why it failed.
/// </summary>
public sealed class KanUnavailableException : ProviderUnavailableException
{
    public KanUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
