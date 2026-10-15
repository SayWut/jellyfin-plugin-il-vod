using Jellyfin.Plugin.IlVod.Aggregation;

namespace Jellyfin.Plugin.IlVod.Tests.Aggregation;

public class ProviderScopedIdTests
{
    [Fact]
    public void RoundTripsAnInnerIdThatItselfContainsTheSeparator()
    {
        ProviderScopedId id = new("mako", "/mako-vod-index?filter=a:b");

        Assert.True(ProviderScopedId.TryParse(id.ToString(), out ProviderScopedId? parsed));
        Assert.Equal(id, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-separator")]
    [InlineData(":inner")]
    [InlineData("mako:")]
    [InlineData("Mako:inner")]
    [InlineData("ma-ko:inner")]
    [InlineData("vcm~channel~gallery")]
    public void RejectsAnythingThatIsNotAValidProviderFollowedByAnInnerId(string? raw)
    {
        Assert.False(ProviderScopedId.TryParse(raw, out _));
    }
}
