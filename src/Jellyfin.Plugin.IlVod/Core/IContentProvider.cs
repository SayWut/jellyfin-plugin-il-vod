namespace Jellyfin.Plugin.IlVod.Core;

/// <summary>One content provider as the channel sees it: who it is, and its content in the shared model.</summary>
public interface IContentProvider
{
    ProviderInfo Info { get; }

    /// <summary>The provider's content, with the provider's own ids and category names — the
    /// channel scopes the ids and maps the categories.</summary>
    ICatalog Catalog { get; }
}
