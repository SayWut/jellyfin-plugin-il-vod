namespace Jellyfin.Plugin.Mako.MakoApi;

/// <summary>
/// The one seam through which the plugin talks to mako: fetch a page or config file, or post an
/// encrypted body and read the response text. Everything about headers, pacing, redirects and the
/// bot challenge lives behind this boundary (<see cref="MakoHttpClient"/>); callers above it never
/// see an <c>HttpClient</c>.
/// </summary>
public interface IMakoHttp
{
    Task<string> GetTextAsync(string url, CancellationToken cancellationToken);

    Task<string> PostTextAsync(string url, string body, string contentType, CancellationToken cancellationToken);
}
