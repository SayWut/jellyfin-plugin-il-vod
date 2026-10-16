namespace Jellyfin.Plugin.IlVod.Providers.Kan.Source;

/// <summary>
/// The one seam through which the plugin talks to Kan's hosts: GET a page, a feed or a playlist.
/// Status codes, timeouts and transport failures stay behind this boundary
/// (<see cref="KanHttpClient"/>), which turns all of them into <see cref="KanUnavailableException"/>;
/// callers above it never see an <c>HttpClient</c>.
/// </summary>
public interface IKanHttp
{
    Task<string> GetTextAsync(string url, CancellationToken cancellationToken);
}
