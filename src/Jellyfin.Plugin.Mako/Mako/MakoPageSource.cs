using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.Mako.Cache;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Mako.MakoApi;

/// <summary>
/// Fetches a mako show/season page through the cache lifetimes spec §2 assigns them (N2), serving
/// a stale copy rather than failing outright when mako refuses (spec §5.4). Parsing the page's
/// JSON belongs to <see cref="MakoPages"/>; deciding what to do with the parsed result — which
/// page to fetch next, how long it may live — belongs to <c>MakoCatalog</c> (task B9).
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
    /// instead of starting its own request; the entry is removed once that fetch settles, success
    /// or failure, so a failure never poisons a later call into forever reusing it.
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
    public async Task<string> GetAsync(string pagePath, Func<string, TimeSpan> lifetimeOf, CancellationToken cancellationToken)
    {
        string cacheKey = CacheKeyFor(pagePath);
        CacheRead cached = _cache.Read(cacheKey);
        if (cached.IsFresh)
        {
            return cached.Value!;
        }

        Task<string> fetch = _inFlightFetches.GetOrAdd(
            pagePath,
            _ => FetchAndCacheAsync(pagePath, cacheKey, lifetimeOf, cancellationToken));
        try
        {
            return await fetch.ConfigureAwait(false);
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
            _inFlightFetches.TryRemove(new KeyValuePair<string, Task<string>>(pagePath, fetch));
        }
    }

    /// <summary>Caches a page fetched under a different path — the show page doubles as its current season's page.</summary>
    public void Remember(string pagePath, string pageJson, TimeSpan lifetime) => _cache.Write(CacheKeyFor(pagePath), pageJson, lifetime);

    /// <summary>
    /// Peeks at whatever copy of <paramref name="pagePath"/> is cached, fresh or stale, without
    /// ever making a request. <c>MakoCatalog.GetShowsAsync</c> (task B9) is built on this so
    /// opening the channel can never cost mako anything (N1).
    /// </summary>
    public bool TryGetCached(string pagePath, [NotNullWhen(true)] out string? json)
    {
        json = _cache.Read(CacheKeyFor(pagePath)).Value;
        return json is not null;
    }

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
