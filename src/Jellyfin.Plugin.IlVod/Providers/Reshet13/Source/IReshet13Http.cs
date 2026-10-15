namespace Jellyfin.Plugin.IlVod.Providers.Reshet13.Source;

/// <summary>
/// The one seam through which the plugin talks to Reshet 13's hosts: POST a JSON body to Kaltura,
/// or GET a manifest. Status codes, timeouts and transport failures stay behind this boundary
/// (<see cref="Reshet13HttpClient"/>), which turns all of them into
/// <see cref="Reshet13UnavailableException"/>; callers above it never see an <c>HttpClient</c>.
/// </summary>
public interface IReshet13Http
{
    Task<string> PostJsonAsync(string url, string body, CancellationToken cancellationToken);

    Task<string> GetTextAsync(string url, CancellationToken cancellationToken);
}
