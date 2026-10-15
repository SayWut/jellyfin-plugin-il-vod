namespace Jellyfin.Plugin.IlVod.Catalog;

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
