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
}
