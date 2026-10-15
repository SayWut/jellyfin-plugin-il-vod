namespace Jellyfin.Plugin.IlVod.Core;

/// <summary>
/// A provider could not answer right now — refused, timed out, or challenged the request. The base
/// of every provider's own "try again later" failure, so the channel can treat them alike without
/// naming any one provider's exception types. Never caught to answer with an empty list: Jellyfin
/// would take that as the folder's full contents.
/// </summary>
public abstract class ProviderUnavailableException : Exception
{
    protected ProviderUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
