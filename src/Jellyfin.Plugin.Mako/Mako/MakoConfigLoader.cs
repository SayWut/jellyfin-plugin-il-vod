using Jellyfin.Plugin.Mako.Cache;

namespace Jellyfin.Plugin.Mako.MakoApi;

/// <summary>
/// Loads mako's three-stage config chain (main -&gt; core -&gt; notes) through the 24-hour cache
/// from spec §5.3, fetching only when at least one of the three has gone stale. <see cref="Forget"/>
/// lets <c>StreamResolver</c> (task B7) force one refetch after a decrypt failure without reaching
/// into the cache itself.
/// </summary>
public sealed class MakoConfigLoader
{
    private const string MainConfigCacheKey = "config:main";
    private const string CoreConfigCacheKey = "config:core";
    private const string NotesConfigCacheKey = "config:notes";

    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    private readonly IMakoHttp _http;
    private readonly TimedCache _cache;
    private readonly Func<string> _readAppVersion;

    public MakoConfigLoader(IMakoHttp http, TimedCache cache, Func<string> readAppVersion)
    {
        _http = http;
        _cache = cache;
        _readAppVersion = readAppVersion;
    }

    public async Task<MakoConfig> LoadAsync(CancellationToken cancellationToken)
    {
        CacheRead main = _cache.Read(MainConfigCacheKey);
        CacheRead core = _cache.Read(CoreConfigCacheKey);
        CacheRead notes = _cache.Read(NotesConfigCacheKey);

        if (main.IsFresh && core.IsFresh && notes.IsFresh)
        {
            return MakoConfig.Parse(main.Value!, core.Value!, notes.Value!);
        }

        return await FetchAndParseAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Marks all three cached files stale, without discarding them, so the next <see cref="LoadAsync"/> refetches.</summary>
    public void Forget()
    {
        ForgetEntry(MainConfigCacheKey);
        ForgetEntry(CoreConfigCacheKey);
        ForgetEntry(NotesConfigCacheKey);
    }

    private async Task<MakoConfig> FetchAndParseAsync(CancellationToken cancellationToken)
    {
        string mainJson = await FetchAndCacheAsync(MakoUrls.MainConfigUrl(_readAppVersion()), MainConfigCacheKey, cancellationToken).ConfigureAwait(false);
        string coreJson = await FetchAndCacheAsync(MakoConfig.CoreConfigUrlOf(mainJson), CoreConfigCacheKey, cancellationToken).ConfigureAwait(false);
        string notesJson = await FetchAndCacheAsync(MakoConfig.NotesUrlOf(coreJson), NotesConfigCacheKey, cancellationToken).ConfigureAwait(false);

        return MakoConfig.Parse(mainJson, coreJson, notesJson);
    }

    private async Task<string> FetchAndCacheAsync(string url, string cacheKey, CancellationToken cancellationToken)
    {
        string json = await _http.GetTextAsync(url, cancellationToken).ConfigureAwait(false);
        _cache.Write(cacheKey, json, Lifetime);
        return json;
    }

    private void ForgetEntry(string cacheKey)
    {
        CacheRead current = _cache.Read(cacheKey);
        if (current.HasValue)
        {
            _cache.Write(cacheKey, current.Value!, TimeSpan.Zero);
        }
    }
}
