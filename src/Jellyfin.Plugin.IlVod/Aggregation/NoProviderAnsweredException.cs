using Jellyfin.Plugin.IlVod.Core;

namespace Jellyfin.Plugin.IlVod.Aggregation;

/// <summary>
/// Every enabled provider failed to list its shows and none had a snapshot to fall back on. The
/// channel's root fails rather than answering empty, which Jellyfin would take as "delete every
/// show".
/// </summary>
public sealed class NoProviderAnsweredException : ProviderUnavailableException
{
    public NoProviderAnsweredException(Exception innerException)
        : base("il-vod: no provider could list its shows", innerException)
    {
    }
}
