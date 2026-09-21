using Jellyfin.Plugin.Mako.MakoApi;

namespace Jellyfin.Plugin.Mako.Tests.Support;

/// <summary>
/// A hand-written fake of <c>IMakoHttp</c>: responses, bot challenges and failures programmed per
/// URL, and a record of every URL asked for, so callers above the HTTP boundary — starting with
/// <c>MakoConfigLoader</c> — can be tested without a real network.
/// </summary>
public sealed class FakeMakoHttp : IMakoHttp
{
    private readonly Dictionary<string, string> _responses = [];
    private readonly HashSet<string> _challengedUrls = [];
    private readonly HashSet<string> _failingUrls = [];
    private readonly List<string> _requests = [];

    public IReadOnlyList<string> Requests => _requests;

    public string? LastPostBody { get; private set; }

    public void Respond(string url, string body) => _responses[url] = body;

    public void Challenge(string url) => _challengedUrls.Add(url);

    public void Fail(string url) => _failingUrls.Add(url);

    public Task<string> GetTextAsync(string url, CancellationToken cancellationToken)
    {
        _requests.Add(url);
        return ResolveAsync(url);
    }

    public Task<string> PostTextAsync(string url, string body, string contentType, CancellationToken cancellationToken)
    {
        _requests.Add(url);
        LastPostBody = body;
        return ResolveAsync(url);
    }

    private Task<string> ResolveAsync(string url)
    {
        if (_challengedUrls.Contains(url))
        {
            return Task.FromException<string>(new BotChallengeException(url));
        }

        if (_failingUrls.Contains(url))
        {
            Uri uri = new(url);
            return Task.FromException<string>(new MakoRequestException($"HTTP 500 for {uri.Host}{uri.AbsolutePath}"));
        }

        return _responses.TryGetValue(url, out string? body)
            ? Task.FromResult(body)
            : Task.FromException<string>(new KeyNotFoundException($"FakeMakoHttp has no response configured for '{url}'"));
    }
}
