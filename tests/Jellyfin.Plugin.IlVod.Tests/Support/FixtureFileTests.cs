namespace Jellyfin.Plugin.IlVod.Tests.Support;

public class FixtureFileTests
{
    [Fact]
    public void ReadsACommittedFixture()
    {
        string text = FixtureFile.ReadText("master.m3u8");

        Assert.StartsWith("#EXTM3U", text, StringComparison.Ordinal);
    }
}
