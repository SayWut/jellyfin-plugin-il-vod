namespace Jellyfin.Plugin.IlVod.Tests;

public class PluginPagesTests
{
    [Fact]
    public void SettingsPageIsEmbeddedUnderTheNameThePluginAdvertises()
    {
        string[] embedded = typeof(Plugin).Assembly.GetManifestResourceNames();

        Assert.Contains(Plugin.SettingsPageResource, embedded);
    }

    [Fact]
    public void PluginGuidMatchesTheBuildMetadata()
    {
        Assert.Equal(Guid.Parse("b8239c51-1963-42d7-82ee-d98ce3e75db4"), Plugin.PluginGuid);
    }

    [Fact]
    public void SettingsPageReferencesTheLivePluginGuid()
    {
        string html = EmbeddedSettingsPageHtml();

        Assert.Contains(Plugin.PluginGuid.ToString(), html, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsPageHasTheCatalogueCacheSettingAndNoPinnedShowSettings()
    {
        string html = EmbeddedSettingsPageHtml();

        Assert.DoesNotContain("PinnedShow", html, StringComparison.Ordinal);
        Assert.Contains("CatalogueCacheHours", html, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsPageListsProvidersAndCategoriesFromThePluginsOwnApi()
    {
        string html = EmbeddedSettingsPageHtml();

        Assert.Contains("'IlVod/Providers'", html, StringComparison.Ordinal);
        Assert.Contains("DisabledProviderIds", html, StringComparison.Ordinal);
        Assert.Contains("CategoryOverrides", html, StringComparison.Ordinal);
    }

    private static string EmbeddedSettingsPageHtml()
    {
        using Stream? resource = typeof(Plugin).Assembly.GetManifestResourceStream(Plugin.SettingsPageResource);
        Assert.NotNull(resource);

        using var reader = new StreamReader(resource!);
        return reader.ReadToEnd();
    }
}
