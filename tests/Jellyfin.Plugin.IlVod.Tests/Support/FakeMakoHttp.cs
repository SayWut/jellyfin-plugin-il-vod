using System.Collections.Concurrent;
using Jellyfin.Plugin.IlVod.MakoApi;

namespace Jellyfin.Plugin.IlVod.Tests.Support;

/// <summary>One request <see cref="FakeMakoHttp"/> saw: its HTTP method and the exact URL asked for.</summary>
public readonly record struct RecordedCall(string Method, string Url);

/// <summary>
/// A hand-written fake of <c>IMakoHttp</c>: responses, bot challenges and failures programmed per
/// URL, and a record of every URL asked for, so callers above the HTTP boundary — starting with
/// <c>MakoConfigLoader</c> — can be tested without a real network. <see cref="GateResponseTo"/>
/// lets a test hold one response open so it can start a second, overlapping call before the first
/// completes.
/// </summary>
public sealed class FakeMakoHttp : IMakoHttp
{
    private const string GetMethod = "GET";
    private const string PostMethod = "POST";

    private readonly Dictionary<string, string> _responses = [];
    private readonly HashSet<string> _challengedUrls = [];
    private readonly HashSet<string> _failingUrls = [];
    private readonly Dictionary<string, TaskCompletionSource> _gates = [];
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _settled = new();
    private readonly List<string> _requests = [];
    private readonly List<RecordedCall> _calls = [];
    private readonly object _requestsLock = new();

    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (_requestsLock)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>Every request seen, with its method — unlike <see cref="Requests"/>, lets a test
    /// tell the token endpoint's POST apart from an ordinary page GET.</summary>
    public IReadOnlyList<RecordedCall> Calls
    {
        get
        {
            lock (_requestsLock)
            {
                return [.. _calls];
            }
        }
    }

    public string? LastPostBody { get; private set; }

    public void Respond(string url, string body) => _responses[url] = body;

    public void Challenge(string url) => _challengedUrls.Add(url);

    public void Fail(string url) => _failingUrls.Add(url);

    /// <summary>
    /// Makes the response to <paramref name="url"/> wait until the returned source is completed,
    /// so a test can prove two calls overlapped instead of assuming it from timing.
    /// </summary>
    public TaskCompletionSource GateResponseTo(string url)
    {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _gates[url] = gate;
        return gate;
    }

    /// <summary>
    /// Completes once this fake has finished with <paramref name="url"/> — response delivered,
    /// challenge or failure thrown — so a test can wait for a fetch that every caller has already
    /// given up on and that it can no longer await through a caller of its own. It is signalled
    /// at the very last moment this fake controls, so that everything still to happen inside the
    /// code under test runs on the completing thread without suspending again.
    /// </summary>
    public Task Settled(string url) => SettledSource(url).Task;

    public async Task<string> GetTextAsync(string url, CancellationToken cancellationToken)
    {
        RecordRequest(GetMethod, url);
        try
        {
            return await ResolveAsync(url, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            SettledSource(url).TrySetResult();
        }
    }

    public async Task<string> PostTextAsync(string url, string body, string contentType, CancellationToken cancellationToken)
    {
        RecordRequest(PostMethod, url);
        LastPostBody = body;
        try
        {
            return await ResolveAsync(url, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            SettledSource(url).TrySetResult();
        }
    }

    private void RecordRequest(string method, string url)
    {
        lock (_requestsLock)
        {
            _requests.Add(url);
            _calls.Add(new RecordedCall(method, url));
        }
    }

    private async Task<string> ResolveAsync(string url, CancellationToken cancellationToken)
    {
        if (_gates.TryGetValue(url, out TaskCompletionSource? gate))
        {
            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_challengedUrls.Contains(url))
        {
            throw new BotChallengeException(url);
        }

        if (_failingUrls.Contains(url))
        {
            throw new MakoRequestException($"HTTP 500 for {MakoUrls.RedactedUrl(url)}");
        }

        return _responses.TryGetValue(url, out string? body)
            ? body
            : throw new KeyNotFoundException($"FakeMakoHttp has no response configured for '{url}'");
    }

    private TaskCompletionSource SettledSource(string url) =>
        _settled.GetOrAdd(url, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
}
