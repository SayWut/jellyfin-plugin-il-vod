namespace Jellyfin.Plugin.Mako.Browsing;

/// <summary>
/// A folder was asked for by Jellyfin itself, not by a person (N1). Thrown rather than answered
/// with an empty list on purpose: <c>ChannelManager.GetChannelItemsInternal</c> (v12.1) treats
/// any answer as the folder's full contents — it caches it, and deletes every item it already
/// holds that the answer lacks, so an empty answer would wipe the episodes a user opened
/// earlier. An exception leaves before either happens, and each of Jellyfin's own callers
/// (<c>Folder</c>, <c>Series</c>, <c>Season</c>) swallows it and carries on with no children.
/// </summary>
public sealed class NotOpenedByAUserException : Exception
{
    public NotOpenedByAUserException(string rawFolderId)
        : base($"mako folder '{rawFolderId}' was asked for by Jellyfin itself, not by a user; nothing is fetched")
    {
    }
}
