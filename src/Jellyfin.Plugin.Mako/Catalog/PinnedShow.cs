namespace Jellyfin.Plugin.Mako.Catalog;

/// <summary>The one show step B plays, and the folder name a user sees for it.</summary>
public sealed record PinnedShow(string PagePath, string Name);
