namespace Jellyfin.Plugin.IlVod.Configuration;

/// <summary>
/// One row of the settings page's category table: shows of provider <see cref="ProviderId"/> that
/// the provider files under <see cref="SourceName"/> are shown under <see cref="SharedName"/>
/// instead of the provider's default mapping. A mutable class with a parameterless constructor,
/// as Jellyfin's XML configuration serializer requires.
/// </summary>
public sealed class CategoryOverride
{
    public string ProviderId { get; set; } = string.Empty;

    public string SourceName { get; set; } = string.Empty;

    public string SharedName { get; set; } = string.Empty;
}
