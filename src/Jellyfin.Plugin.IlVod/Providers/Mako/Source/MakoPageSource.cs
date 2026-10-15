using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.IlVod.Cache;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IlVod.Providers.Mako.Source;

/// <summary>
/// Fetches a mako show/season page through the cache lifetimes spec §2 assigns them (N2), serving
/// a stale copy rather than failing outright when mako refuses (spec §5.4). Parsing the page's
/// JSON belongs to <see cref="MakoPages"/>; deciding what to do with the parsed result — which
/// page to fetch next, how long it may live — belongs to <c>MakoSource</c>.
/// </summary>
public sealed class MakoPageSource
{
    private const string CacheKeyPrefix = "page:";

    private readonly IMakoHttp _http;
    private readonly MakoConfigLoader _config;
    private readonly TimedCache _cache;
    private readonly ILogger<MakoPageSource> _logger;

    /// <summary>
    /// One fetch in flight per page path (N4: at most one request to the bot-protected host at a
    /// time). A second caller for the same uncached path joins the same <see cref="Task{TResult}"/>
    /// instead of starting its own request. The entry is removed once the shared fetch itself has
    /// settled — never merely because one caller's own wait on it was cancelled, since the fetch,
    /// and every other caller still relying on it, keeps running regardless. Two mechanisms
    /// together make that exhaustive, and both remove only the exact task they published, so
    /// neither can evict a newer fetch: <see cref="StartFetch"/>'s continuation covers a fetch
    /// that settles after the entry was published, and <see cref="GetAsync"/>'s <c>finally</c>
    /// covers a fetch that had already settled by the time it was.
    /// </summary>
    private readonly ConcurrentDictionary<string, Task<string>> _inFlightFetches = new();

    public MakoPageSource(IMakoHttp http, MakoConfigLoader config, TimedCache cache, ILogger<MakoPageSource> logger)
    {
        _http = http;
        _config = config;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Returns a fresh cached copy of <paramref name="pagePath"/> without a request. Otherwise
    /// fetches it, caching the result for whatever <paramref name="lifetimeOf"/> — given the
    /// fetched JSON — decides. A refetch that fails with <see cref="BotChallengeException"/> or
    /// <see cref="MakoRequestException"/> falls back to a stale cached copy when one exists, and
    /// rethrows when it does not.
    /// </summary>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was already cancelled and nothing fresh was cached —
    /// checked before joining or starting a fetch, so a caller that has already given up never
    /// waits on, or starts, one. A caller that cancels while a fetch it joined is still running
    /// only ever fails its own wait: the shared fetch itself is never bound to any one caller's
    /// token (it runs with <see cref="CancellationToken.None"/>, bounded only by the HTTP client's
    /// own timeout), so it — and every other caller still sharing it — keeps running regardless.
    /// </exception>
    public async Task<string> GetAsync(string pagePath, Func<string, TimeSpan> lifetimeOf, CancellationToken cancellationToken)
    {
        string cacheKey = CacheKeyFor(pagePath);
        CacheRead cached = _cache.Read(cacheKey);
        if (cached.IsFresh)
        {
            return cached.Value!;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // lifetimeOf is likewise taken from whichever caller happens to start the fetch: every
        // caller asking for the same page path always passes an equivalent function in this
        // codebase (MakoSource decides it from the page path alone), so that is acceptable.
        Task<string> fetch = _inFlightFetches.GetOrAdd(pagePath, _ => StartFetch(pagePath, cacheKey, lifetimeOf));

        try
        {
            return await fetch.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is BotChallengeException or MakoRequestException)
        {
            if (!cached.HasValue)
            {
                throw;
            }

            _logger.LogWarning(exception, "mako: serving a stale copy of {PagePath} after mako refused a refetch", pagePath);
            return cached.Value!;
        }
        finally
        {
            // Only once the shared fetch has actually settled — a caller whose own WaitAsync
            // ended because of ITS OWN token (fetch.IsCompleted still false) must not touch the
            // entry every other caller sharing that same fetch still needs. This is what catches
            // a fetch that settled before GetOrAdd had published it, which StartFetch's own
            // continuation ran too early to see.
            if (fetch.IsCompleted)
            {
                Forget(_inFlightFetches, pagePath, fetch);
            }
        }
    }

    /// <summary>Caches a page fetched under a different path — the show page doubles as its current season's page.</summary>
    public void Remember(string pagePath, string pageJson, TimeSpan lifetime) => _cache.Write(CacheKeyFor(pagePath), pageJson, lifetime);

    /// <summary>
    /// Peeks at whatever copy of <paramref name="pagePath"/> is cached, fresh or stale, without
    /// ever making a request. <c>MakoSource.ShowNameOf</c> is built on this so naming a
    /// season's episodes after their show never costs an index request (N1).
    /// </summary>
    public bool TryGetCached(string pagePath, [NotNullWhen(true)] out string? json)
    {
        json = _cache.Read(CacheKeyFor(pagePath)).Value;
        return json is not null;
    }

    /// <summary>
    /// Starts the shared fetch and makes it clear its own coalescing entry the moment it settles.
    /// Without that, a fetch whose only caller gave up has nobody left to notice it finished: the
    /// settled task would stay behind and, once the cached page expired, answer the next caller
    /// with its old json — or replay its old failure — without asking mako at all.
    /// </summary>
    private Task<string> StartFetch(string pagePath, string cacheKey, Func<string, TimeSpan> lifetimeOf)
    {
        Task<string> fetch = FetchAndCacheAsync(pagePath, cacheKey, lifetimeOf, CancellationToken.None);

        _ = fetch.ContinueWith(
            static (settled, state) =>
            {
                (ConcurrentDictionary<string, Task<string>> inFlightFetches, string pagePath) =
                    ((ConcurrentDictionary<string, Task<string>>, string))state!;
                Forget(inFlightFetches, pagePath, settled);
            },
            (_inFlightFetches, pagePath),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return fetch;
    }

    /// <summary>Removes <paramref name="pagePath"/>'s entry only while it still holds exactly
    /// <paramref name="fetch"/>, so a fetch settling late can never evict a newer one.</summary>
    private static void Forget(ConcurrentDictionary<string, Task<string>> inFlightFetches, string pagePath, Task<string> fetch) =>
        inFlightFetches.TryRemove(new KeyValuePair<string, Task<string>>(pagePath, fetch));

    private async Task<string> FetchAndCacheAsync(string pagePath, string cacheKey, Func<string, TimeSpan> lifetimeOf, CancellationToken cancellationToken)
    {
        MakoConfig config = await _config.LoadAsync(cancellationToken).ConfigureAwait(false);
        string url = MakoUrls.Page(config.BaseUrlTemplate, pagePath);
        string json = await _http.GetTextAsync(url, cancellationToken).ConfigureAwait(false);
        _cache.Write(cacheKey, json, lifetimeOf(json));
        return json;
    }

    private static string CacheKeyFor(string pagePath) => $"{CacheKeyPrefix}{pagePath}";
}
