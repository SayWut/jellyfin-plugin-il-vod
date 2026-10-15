using Jellyfin.Plugin.IlVod.Transcoding;

namespace Jellyfin.Plugin.IlVod.Tests.Transcoding;

public class ProbeSizeLimiterTests
{
    [Fact]
    public void ReplacesTheProbeSizeValueAndLeavesEverythingElseByteIdentical()
    {
        const string original =
            "-analyzeduration 3000000 -probesize 1G -i \"https://cdn.test/a_,850,550,.mp4.csmil/master.m3u8?hdnea=st=1~exp=2~acl=/*~hmac=ab\" -map 0:0 -codec:v:0 copy -f hls \"out.m3u8\"";
        const string expected =
            "-analyzeduration 3000000 -probesize 20M -i \"https://cdn.test/a_,850,550,.mp4.csmil/master.m3u8?hdnea=st=1~exp=2~acl=/*~hmac=ab\" -map 0:0 -codec:v:0 copy -f hls \"out.m3u8\"";

        Assert.Equal(expected, ProbeSizeLimiter.Limit(original));
    }

    [Fact]
    public void LeavesACommandWithNoProbeSizeUnchanged()
    {
        const string original = "-i \"https://cdn.test/master.m3u8\" -map 0:0 -codec:v:0 copy -f hls \"out.m3u8\"";

        Assert.Equal(original, ProbeSizeLimiter.Limit(original));
    }

    [Fact]
    public void ReplacesEveryOccurrenceWhenThereAreTwo()
    {
        const string original = "-probesize 1G -i \"input1.m3u8\" -probesize 500k -i \"input2.m3u8\"";
        const string expected = "-probesize 20M -i \"input1.m3u8\" -probesize 20M -i \"input2.m3u8\"";

        Assert.Equal(expected, ProbeSizeLimiter.Limit(original));
    }

    [Fact]
    public void DoesNotTouchAUrlThatHappensToContainTheWordProbesizeAsAQueryValue()
    {
        const string original = "-i \"https://cdn.test/x?x=-probesizeish\" -probesize 1G -f hls \"out.m3u8\"";
        const string expected = "-i \"https://cdn.test/x?x=-probesizeish\" -probesize 20M -f hls \"out.m3u8\"";

        Assert.Equal(expected, ProbeSizeLimiter.Limit(original));
    }
}
