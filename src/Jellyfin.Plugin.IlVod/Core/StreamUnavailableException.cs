namespace Jellyfin.Plugin.IlVod.Core;

public sealed class StreamUnavailableException : Exception
{
    public StreamUnavailableException(string message)
        : base(message)
    {
    }

    public StreamUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
