using Jellyfin.Plugin.IlVod.Net;
using Jellyfin.Plugin.IlVod.Providers.Kan.Source;

namespace Jellyfin.Plugin.IlVod.Tests.Support;

/// <summary>
/// A hand-written fake of <c>IKanHttp</c>: answers each URL with the text it was given, or fails
/// it the way <c>KanHttpClient</c> fails a refused request. Records every URL asked for, safely
/// across the rails a catalogue walk reads at once.
/// </summary>
public sealed class FakeKanHttp : IKanHttp
{
    private readonly Dictionary<string, string> _answers = [];
    private readonly HashSet<string> _failing = [];
    private readonly List<string> _requests = [];
    private readonly object _lock = new();

    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (_lock)
            {
                return [.. _requests];
            }
        }
    }

    public void RespondTo(string url, string text)
    {
        lock (_lock)
        {
            _failing.Remove(url);
            _answers[url] = text;
        }
    }

    public void Fail(string url)
    {
        lock (_lock)
        {
            _failing.Add(url);
        }
    }

    public Task<string> GetTextAsync(string url, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _requests.Add(url);
            if (_failing.Contains(url))
            {
                throw new KanUnavailableException($"HTTP 503 for {SafeUrl.Redacted(url)}");
            }

            return _answers.TryGetValue(url, out string? text)
                ? Task.FromResult(text)
                : throw new KeyNotFoundException($"FakeKanHttp has no answer for GET {url}");
        }
    }
}
