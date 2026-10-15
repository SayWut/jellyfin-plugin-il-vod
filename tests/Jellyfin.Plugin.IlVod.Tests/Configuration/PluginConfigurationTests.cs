using Jellyfin.Plugin.IlVod.Configuration;

namespace Jellyfin.Plugin.IlVod.Tests.Configuration;

/// <summary>
/// The defaults a freshly installed plugin plays with, before a user ever opens the settings page
/// (spec §5): every provider on, no category overrides, mako's app version and the five cache
/// lifetimes (spec §5.3, §6.3), Reshet 13's two cache lifetimes, as whole numbers so the settings page never has to serialise a
/// <see cref="TimeSpan"/>.
/// </summary>
public class PluginConfigurationTests
{
    [Fact]
    public void DefaultsMatchTheSpecForTheAppVersionAndLifetimes()
    {
        var configuration = new PluginConfiguration();

        Assert.Empty(configuration.DisabledProviderIds);
        Assert.Empty(configuration.CategoryOverrides);
        Assert.Equal("43.0", configuration.Mako.AppVersion);
        Assert.Equal(24, configuration.Mako.CatalogueCacheHours);
        Assert.Equal(12, configuration.Mako.ShowPageCacheHours);
        Assert.Equal(7, configuration.Mako.PastSeasonCacheDays);
        Assert.Equal(6, configuration.Mako.CurrentSeasonCacheHours);
        Assert.Equal(24, configuration.Reshet13.CatalogueCacheHours);
        Assert.Equal(6, configuration.Reshet13.ShowCacheHours);
        Assert.Equal(5, configuration.StreamLinkCacheMinutes);
    }

    /// <summary>A settings page or config file that leaves a list or section out must not leave
    /// the plugin holding a null it will dereference on the next channel open.</summary>
    [Fact]
    public void AMissingListOrSectionReadsAsEmptyOrDefaultNeverNull()
    {
        PluginConfiguration configuration = new() { DisabledProviderIds = null!, CategoryOverrides = null!, Mako = null!, Reshet13 = null! };

        Assert.Empty(configuration.DisabledProviderIds);
        Assert.Empty(configuration.CategoryOverrides);
        Assert.Equal("43.0", configuration.Mako.AppVersion);
        Assert.Equal(24, configuration.Reshet13.CatalogueCacheHours);
    }

    [Fact]
    public void TheConfigurationRoundTripsThroughTheXmlSerializerJellyfinUses()
    {
        PluginConfiguration configuration = new()
        {
            DisabledProviderIds = ["mako"],
            CategoryOverrides = [new CategoryOverride { ProviderId = "mako", SourceName = "דוקומנטרי", SharedName = "דוקו" }],
            Mako = new MakoSettings { AppVersion = "44.0" },
            Reshet13 = new Reshet13Settings { CatalogueCacheHours = 48 },
        };
        System.Xml.Serialization.XmlSerializer serializer = new(typeof(PluginConfiguration));
        using StringWriter writer = new();
        serializer.Serialize(writer, configuration);

        using StringReader reader = new(writer.ToString());
        PluginConfiguration read = (PluginConfiguration)serializer.Deserialize(reader)!;

        Assert.Equal(["mako"], read.DisabledProviderIds);
        CategoryOverride row = Assert.Single(read.CategoryOverrides);
        Assert.Equal(("mako", "דוקומנטרי", "דוקו"), (row.ProviderId, row.SourceName, row.SharedName));
        Assert.Equal("44.0", read.Mako.AppVersion);
        Assert.Equal(48, read.Reshet13.CatalogueCacheHours);
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
