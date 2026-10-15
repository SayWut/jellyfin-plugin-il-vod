using Jellyfin.Plugin.IlVod.Providers.Reshet13.Source;

namespace Jellyfin.Plugin.IlVod.Tests.Support;

/// <summary>One request <see cref="FakeReshet13Http"/> saw: its method, URL and (for a POST) body.</summary>
public sealed record Reshet13Call(string Method, string Url, string? Body);

/// <summary>
/// A hand-written fake of <c>IReshet13Http</c>. Every Kaltura call is a POST to one of a few URLs,
/// told apart only by its body, so a POST answer is programmed per URL <em>and</em> body predicate;
/// the first matching rule answers. A rule given several answers replays them in order and then
/// repeats its last one, so a test can script "an error, then success". Records every call.
/// </summary>
public sealed class FakeReshet13Http : IReshet13Http
{
    private const string GetMethod = "GET";
    private const string PostMethod = "POST";

    private readonly List<PostRule> _postRules = [];
    private readonly Dictionary<string, string> _getResponses = [];
    private readonly HashSet<string> _failingGets = [];
    private readonly List<Reshet13Call> _calls = [];
    private readonly object _callsLock = new();

    public IReadOnlyList<Reshet13Call> Calls
    {
        get
        {
            lock (_callsLock)
            {
                return [.. _calls];
            }
        }
    }

    public IReadOnlyList<Reshet13Call> Posts => [.. Calls.Where(call => call.Method == PostMethod)];

    public IReadOnlyList<Reshet13Call> Gets => [.. Calls.Where(call => call.Method == GetMethod)];

    public void RespondToPost(string url, Func<string, bool> bodyMatches, params string[] answersInOrder) =>
        _postRules.Add(new PostRule(url, bodyMatches, new Queue<string>(answersInOrder), Failing: false));

    /// <summary>Makes a matching POST throw the way <c>Reshet13HttpClient</c> does for a failed request.</summary>
    public void FailPost(string url, Func<string, bool> bodyMatches) =>
        _postRules.Insert(0, new PostRule(url, bodyMatches, new Queue<string>(), Failing: true));

    /// <summary>Takes back every rule for <paramref name="url"/> whose predicate matches <paramref name="sampleBody"/>.</summary>
    public void ForgetPost(string url, string sampleBody) =>
        _postRules.RemoveAll(rule => rule.Url == url && rule.BodyMatches(sampleBody));

    public void RespondToGet(string url, string text) => _getResponses[url] = text;

    public void FailGet(string url) => _failingGets.Add(url);

    public Task<string> PostJsonAsync(string url, string body, CancellationToken cancellationToken)
    {
        Record(new Reshet13Call(PostMethod, url, body));
        PostRule rule = _postRules.FirstOrDefault(candidate => candidate.Url == url && candidate.BodyMatches(body))
            ?? throw new KeyNotFoundException($"FakeReshet13Http has no answer for POST {url} with body {body}");

        if (rule.Failing)
        {
            throw new Reshet13UnavailableException($"HTTP 500 for {Reshet13Urls.RedactedUrl(url)}");
        }

        return Task.FromResult(rule.Answers.Count > 1 ? rule.Answers.Dequeue() : rule.Answers.Peek());
    }

    public Task<string> GetTextAsync(string url, CancellationToken cancellationToken)
    {
        Record(new Reshet13Call(GetMethod, url, null));
        if (_failingGets.Contains(url))
        {
            throw new Reshet13UnavailableException($"HTTP 500 for {Reshet13Urls.RedactedUrl(url)}");
        }

        return _getResponses.TryGetValue(url, out string? text)
            ? Task.FromResult(text)
            : throw new KeyNotFoundException($"FakeReshet13Http has no answer for GET {url}");
    }

    private void Record(Reshet13Call call)
    {
        lock (_callsLock)
        {
            _calls.Add(call);
        }
    }

    private sealed record PostRule(string Url, Func<string, bool> BodyMatches, Queue<string> Answers, bool Failing);
}
