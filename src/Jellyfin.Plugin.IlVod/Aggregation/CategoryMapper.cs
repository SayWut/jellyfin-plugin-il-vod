using Jellyfin.Plugin.IlVod.Configuration;
using Jellyfin.Plugin.IlVod.Core;

namespace Jellyfin.Plugin.IlVod.Aggregation;

/// <summary>
/// Turns a provider's category names into the shared ones: the settings page's override for that
/// provider and name first, then the provider's own default mapping, else the name unchanged.
/// Read on every call, so a saved override applies to the next channel open with no restart.
/// </summary>
public sealed class CategoryMapper
{
    private readonly Func<IEnumerable<CategoryOverride>> _readOverrides;

    public CategoryMapper(Func<IEnumerable<CategoryOverride>> readOverrides)
    {
        _readOverrides = readOverrides;
    }

    /// <summary>Two provider categories that map to the same shared one are listed once, in the
    /// order the provider first names them.</summary>
    public IReadOnlyList<string> Map(ProviderInfo provider, IReadOnlyList<string> categories)
    {
        if (categories.Count == 0)
        {
            return categories;
        }

        Dictionary<string, string> overrides = OverridesFor(provider.Id);
        return categories
            .Select(category => overrides.GetValueOrDefault(category)
                ?? provider.DefaultCategories.GetValueOrDefault(category)
                ?? category)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>A row with no shared name is a row still being filled in on the settings page,
    /// not an instruction to blank the category, so it is ignored.</summary>
    private Dictionary<string, string> OverridesFor(string providerId)
    {
        Dictionary<string, string> overrides = new(StringComparer.Ordinal);
        foreach (CategoryOverride row in _readOverrides())
        {
            string sourceName = row.SourceName?.Trim() ?? string.Empty;
            string sharedName = row.SharedName?.Trim() ?? string.Empty;
            if (row.ProviderId == providerId && sourceName.Length > 0 && sharedName.Length > 0)
            {
                overrides[sourceName] = sharedName;
            }
        }

        return overrides;
    }
}
