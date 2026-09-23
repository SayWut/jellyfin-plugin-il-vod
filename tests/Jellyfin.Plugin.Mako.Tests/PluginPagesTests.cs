namespace Jellyfin.Plugin.Mako.Tests;

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
        Assert.Equal(Guid.Parse("1b35ed7d-7558-4592-aa1a-681b9d67d305"), Plugin.PluginGuid);
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

    private static string EmbeddedSettingsPageHtml()
    {
        using Stream? resource = typeof(Plugin).Assembly.GetManifestResourceStream(Plugin.SettingsPageResource);
        Assert.NotNull(resource);

        using var reader = new StreamReader(resource!);
        return reader.ReadToEnd();
    }
}
