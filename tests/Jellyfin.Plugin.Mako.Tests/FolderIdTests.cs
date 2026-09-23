using Jellyfin.Plugin.Mako.Browsing;

namespace Jellyfin.Plugin.Mako.Tests;

public class FolderIdTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingIdMeansTheChannelRoot(string? raw)
    {
        Assert.Equal(FolderId.Root, FolderId.Parse(raw));
    }

    [Fact]
    public void ShowIdSurvivesARoundTrip()
    {
        FolderId original = FolderId.ForShow("/mako-vod-keshet/shemesh");

        FolderId parsed = FolderId.Parse(original.ToString());

        Assert.Equal(FolderKind.Show, parsed.Kind);
        Assert.Equal("/mako-vod-keshet/shemesh", parsed.Value);
    }

    [Fact]
    public void RootSerialisesToAnEmptyString()
    {
        Assert.Equal(string.Empty, FolderId.Root.ToString());
    }

    [Theory]
    [InlineData("shemesh")]
    [InlineData("movie|123")]
    [InlineData("|shemesh")]
    public void UnknownFormatIsRejected(string raw)
    {
        Assert.Throws<FormatException>(() => FolderId.Parse(raw));
    }

    [Fact]
    public void AShowIdUsesTheNewPrefixSoJellyfinCreatesEveryShowAfreshWithItsGenres()
    {
        Assert.Equal("mako-series|/mako-vod-keshet/shemesh", FolderId.ForShow("/mako-vod-keshet/shemesh").ToString());
    }

    [Fact]
    public void ASeasonIdCarriesItsShowsPathAndRoundTrips()
    {
        FolderId season = FolderId.ForSeason("/mako-vod-keshet/shemesh", "/mako-vod-keshet/shemesh-s1");

        Assert.Equal("mako-season|/mako-vod-keshet/shemesh|/mako-vod-keshet/shemesh-s1", season.ToString());
        Assert.Equal(season, FolderId.Parse(season.ToString()));
        Assert.Equal(("/mako-vod-keshet/shemesh", "/mako-vod-keshet/shemesh-s1"), season.SeasonParts());
    }

    [Theory]
    [InlineData("mako-season|/only-one-part")]
    [InlineData("mako-season|/a|/b|/c")]
    [InlineData("series|/mako-vod-keshet/shemesh")]
    [InlineData("series-season|/mako-vod-keshet/shemesh-s1")]
    public void OldPrefixesAndMalformedSeasonValuesAreNotFolderIds(string raw)
    {
        Assert.Throws<FormatException>(() => FolderId.Parse(raw));
    }

    [Fact]
    public void SeasonPartsOfANonSeasonIsAProgrammingError()
    {
        Assert.Throws<InvalidOperationException>(() => FolderId.ForShow("/x").SeasonParts());
    }
}
