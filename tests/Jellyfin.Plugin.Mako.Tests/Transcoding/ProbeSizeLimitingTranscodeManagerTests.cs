using Jellyfin.Plugin.Mako.Browsing;
using Jellyfin.Plugin.Mako.Model;
using Jellyfin.Plugin.Mako.Transcoding;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.Mako.Tests.Transcoding;

public class ProbeSizeLimitingTranscodeManagerTests
{
    private const string OriginalArguments = "-probesize 1G -i \"in.m3u8\"";

    [Fact]
    public async Task RewritesTheProbeSizeWhenTheStreamIsMakosOwn()
    {
        var recorder = new CallRecorder();
        var fake = new FakeTranscodeManager(NullLoggerFactory.Instance, recorder);
        var sut = new ProbeSizeLimitingTranscodeManager(fake, NullLogger<ProbeSizeLimitingTranscodeManager>.Instance);
        StreamState state = StreamStateFor(MediaSourceFactory.Create(new StreamLink("https://cdn.test/master.m3u8", "hls")));

        await sut.StartFfMpeg(state, "out.ts", OriginalArguments, Guid.NewGuid(), TranscodingJobType.Hls, new CancellationTokenSource());

        Assert.Equal("-probesize 20M -i \"in.m3u8\"", recorder.LastCommandLineArguments);
    }

    [Fact]
    public async Task PassesTheOriginalArgumentsUnchangedForAnyOtherStream()
    {
        var recorder = new CallRecorder();
        var fake = new FakeTranscodeManager(NullLoggerFactory.Instance, recorder);
        var sut = new ProbeSizeLimitingTranscodeManager(fake, NullLogger<ProbeSizeLimitingTranscodeManager>.Instance);
        StreamState state = StreamStateFor(new MediaSourceInfo { Name = "some-other-plugin" });

        await sut.StartFfMpeg(state, "out.ts", OriginalArguments, Guid.NewGuid(), TranscodingJobType.Hls, new CancellationTokenSource());

        Assert.Same(OriginalArguments, recorder.LastCommandLineArguments);
    }

    [Fact]
    public void DelegatesPingTranscodingJobToTheInnerManager()
    {
        var recorder = new CallRecorder();
        var fake = new FakeTranscodeManager(NullLoggerFactory.Instance, recorder);
        var sut = new ProbeSizeLimitingTranscodeManager(fake, NullLogger<ProbeSizeLimitingTranscodeManager>.Instance);

        sut.PingTranscodingJob("session-1", true);

        Assert.Equal("session-1", recorder.LastPingedPlaySessionId);
    }

    [Fact]
    public void DelegatesGetTranscodingJobByPlaySessionIdToTheInnerManager()
    {
        var recorder = new CallRecorder();
        var fake = new FakeTranscodeManager(NullLoggerFactory.Instance, recorder);
        var sut = new ProbeSizeLimitingTranscodeManager(fake, NullLogger<ProbeSizeLimitingTranscodeManager>.Instance);

        sut.GetTranscodingJob("session-2");

        Assert.Equal("session-2", recorder.LastRequestedPlaySessionId);
    }

    [Fact]
    public void DisposingTheDecoratorDisposesADisposableInner()
    {
        var recorder = new CallRecorder();
        var fake = new FakeTranscodeManager(NullLoggerFactory.Instance, recorder);
        var sut = new ProbeSizeLimitingTranscodeManager(fake, NullLogger<ProbeSizeLimitingTranscodeManager>.Instance);

        sut.Dispose();

        Assert.True(recorder.WasDisposed);
    }

    // Constructs the same way Jellyfin does: MediaSourceManager and the outer transcode
    // manager are irrelevant to picking a probe size, so they are left null. Verified against
    // the real StreamState constructor in Jellyfin.Controller 12.1.0 by reflection: it stores
    // both without dereferencing them, and MediaSource is a plain settable property.
    private static StreamState StreamStateFor(MediaSourceInfo mediaSource) =>
        new(mediaSourceManager: null!, TranscodingJobType.Hls, transcodeManager: null!) { MediaSource = mediaSource };
}
