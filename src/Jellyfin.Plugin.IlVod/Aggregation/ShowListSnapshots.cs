using System.Text.Json;
using Jellyfin.Plugin.IlVod.Cache;
using Jellyfin.Plugin.IlVod.Core;

namespace Jellyfin.Plugin.IlVod.Aggregation;

/// <summary>
/// Each provider's last show list that loaded, persisted so it survives a restart. Read only when
/// that provider fails while the channel is opening: Jellyfin takes the root answer as the
/// channel's full contents and deletes whatever it lacks, so a provider that is down for an hour
/// must keep answering with what it last had rather than vanish — and lose its users' watched
/// marks with it. Stored as the provider spelled it (before category mapping), so a change of
/// mapping applies to a snapshot too.
/// </summary>
public sealed class ShowListSnapshots
{
    private const string KeyPrefix = "shows:";

    // A snapshot is never "fresh" — it is a fallback, read whatever its age; TimedCache keeps a
    // stale entry readable forever.
    private static readonly TimeSpan NeverFresh = TimeSpan.Zero;

    private readonly TimedCache _cache;

    public ShowListSnapshots(TimedCache cache)
    {
        _cache = cache;
    }

    public void Save(string providerId, IReadOnlyList<Show> shows)
    {
        string key = KeyPrefix + providerId;
        string json = JsonSerializer.Serialize(shows);

        // Skips the rewrite of the whole snapshot file when nothing changed, which is the usual case.
        if (_cache.Read(key).Value != json)
        {
            _cache.Write(key, json, NeverFresh);
        }
    }

    public IReadOnlyList<Show>? TryGet(string providerId)
    {
        string? json = _cache.Read(KeyPrefix + providerId).Value;
        if (json is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<Show>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
