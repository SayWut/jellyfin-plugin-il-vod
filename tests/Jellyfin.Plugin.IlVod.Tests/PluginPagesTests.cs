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

    /// <summary>
    /// jellyfin-web 12.1 registers its emby-* elements through an old custom-elements polyfill
    /// whose patched <c>document.createElement</c> takes the type extension as a string: given
    /// <c>{ is: 'emby-input' }</c> it throws, which left the settings page loading forever. The page
    /// builds those elements from markup instead.
    /// </summary>
    [Fact]
    public void SettingsPageNeverPassesAnOptionsObjectToCreateElement()
    {
        string html = EmbeddedSettingsPageHtml();

        Assert.DoesNotMatch(new System.Text.RegularExpressions.Regex(@"createElement\([^)]*,\s*\{"), html);
    }

    /// <summary>Whatever fails while the page loads, the loading animation must stop.</summary>
    [Fact]
    public void SettingsPageStopsLoadingWhenLoadingFails()
    {
        string html = EmbeddedSettingsPageHtml();

        Assert.Contains(".catch(", html, StringComparison.Ordinal);
    }

    private static string EmbeddedSettingsPageHtml()
    {
        using Stream? resource = typeof(Plugin).Assembly.GetManifestResourceStream(Plugin.SettingsPageResource);
        Assert.NotNull(resource);

        using var reader = new StreamReader(resource!);
        return reader.ReadToEnd();
    }
}
