using Jellyfin.Plugin.IlVod.Providers.Kan.Source;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Kan;

public class KanDurationTests
{
    [Theory]
    [InlineData("00:29:35", 1775)]
    [InlineData("00:29:35\t", 1775)]
    [InlineData("00:29:35 ", 1775)]
    [InlineData("00:29:35.", 1775)]
    [InlineData("00:27:49.000", 1669)]
    [InlineData("00:49:27:00", 2967)]
    [InlineData("01:34:00", 5640)]
    [InlineData("29:35", 1775)]
    [InlineData("1:00:23", 3623)]
    [InlineData("42", 42)]
    public void EverySpellingKanUsesIsRead(string text, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), KanDuration.Parse(text));

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("about an hour")]
    [InlineData("1:2:3:4:5")]
    [InlineData("00:-1:00")]
    public void ZeroOrNothingOrNonsenseIsNoLength(string? text) =>
        Assert.Null(KanDuration.Parse(text));
}
