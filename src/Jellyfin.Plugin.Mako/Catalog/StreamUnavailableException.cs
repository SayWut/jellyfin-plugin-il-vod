namespace Jellyfin.Plugin.Mako.Catalog;

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
