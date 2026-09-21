using Jellyfin.Plugin.Mako.Configuration;

namespace Jellyfin.Plugin.Mako.Tests.Configuration;

/// <summary>
/// The defaults a freshly installed plugin plays with, before a user ever opens the settings page
/// (spec §5): the pinned show and the four cache lifetimes (spec §5.3), as whole numbers so the
/// settings page (task B10) never has to serialise a <see cref="TimeSpan"/>.
/// </summary>
public class PluginConfigurationTests
{
    [Fact]
    public void DefaultsMatchTheSpecForThePinnedShowAndAppVersion()
    {
        var configuration = new PluginConfiguration();

        Assert.Equal("/mako-vod-keshet/shemesh", configuration.PinnedShowPath);
        Assert.Equal("שמש", configuration.PinnedShowName);
        Assert.Equal("43.0", configuration.AppVersion);
    }

    [Fact]
    public void DefaultsMatchTheSpecForEveryCacheLifetime()
    {
        var configuration = new PluginConfiguration();

        Assert.Equal(12, configuration.ShowPageCacheHours);
        Assert.Equal(7, configuration.PastSeasonCacheDays);
        Assert.Equal(6, configuration.CurrentSeasonCacheHours);
        Assert.Equal(5, configuration.StreamLinkCacheMinutes);
    }

    [Fact]
    public void NoStepAOnlySettingsSurvive()
    {
        PluginConfiguration configuration = new();
        string[] propertyNames = configuration.GetType().GetProperties().Select(property => property.Name).ToArray();

        Assert.DoesNotContain("TestStreamUrl", propertyNames);
        Assert.DoesNotContain("TestStreamContainer", propertyNames);
    }
}
