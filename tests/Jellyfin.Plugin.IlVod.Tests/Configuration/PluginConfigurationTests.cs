using Jellyfin.Plugin.IlVod.Configuration;

namespace Jellyfin.Plugin.IlVod.Tests.Configuration;

/// <summary>
/// The defaults a freshly installed plugin plays with, before a user ever opens the settings page
/// (spec §5): the app version and the five cache lifetimes (spec §5.3, §6.3), as whole numbers so
/// the settings page (task B10, C5) never has to serialise a <see cref="TimeSpan"/>.
/// </summary>
public class PluginConfigurationTests
{
    [Fact]
    public void DefaultsMatchTheSpecForTheAppVersionAndLifetimes()
    {
        var configuration = new PluginConfiguration();

        Assert.Equal("43.0", configuration.AppVersion);
        Assert.Equal(24, configuration.CatalogueCacheHours);
        Assert.Equal(12, configuration.ShowPageCacheHours);
        Assert.Equal(7, configuration.PastSeasonCacheDays);
        Assert.Equal(6, configuration.CurrentSeasonCacheHours);
        Assert.Equal(5, configuration.StreamLinkCacheMinutes);
    }

    [Fact]
    public void ThePinnedShowSettingsAreGone()
    {
        Assert.Null(typeof(PluginConfiguration).GetProperty("PinnedShowPath"));
        Assert.Null(typeof(PluginConfiguration).GetProperty("PinnedShowName"));
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
