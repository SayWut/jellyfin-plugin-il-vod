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
    public void SeasonIdSurvivesARoundTrip()
    {
        FolderId parsed = FolderId.Parse(FolderId.ForSeason("shemesh-s1").ToString());

        Assert.Equal(FolderKind.Season, parsed.Kind);
        Assert.Equal("shemesh-s1", parsed.Value);
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
}
