namespace Jellyfin.Plugin.IlVod.Core;

/// <summary>
/// One content provider as the channel sees it: who it is, its content in the shared model, and
/// how its own category names map onto the shared ones by default.
/// </summary>
public interface IContentProvider
{
    ProviderInfo Info { get; }

    /// <summary>The provider's content, with the provider's own ids — the channel scopes them.</summary>
    ICatalog Catalog { get; }

    /// <summary>
    /// The provider's category name → the shared category name it means. A name missing here is
    /// shown as the provider spells it; the settings page can override any entry.
    /// </summary>
    IReadOnlyDictionary<string, string> DefaultCategories { get; }
}
