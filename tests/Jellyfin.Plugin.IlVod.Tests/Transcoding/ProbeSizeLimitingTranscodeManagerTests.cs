using Jellyfin.Plugin.IlVod.Browsing;
using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Providers.Mako;
using Jellyfin.Plugin.IlVod.Transcoding;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.IlVod.Tests.Transcoding;

public class ProbeSizeLimitingTranscodeManagerTests
{
    private const string OriginalArguments = "-probesize 1G -i \"in.m3u8\"";

    [Fact]
    public async Task RewritesTheProbeSizeWhenTheStreamIsMakosOwn()
    {
        var recorder = new CallRecorder();
        var fake = new FakeTranscodeManager(NullLoggerFactory.Instance, recorder);
        var sut = new ProbeSizeLimitingTranscodeManager(fake, [MakoProvider.Description], NullLogger<ProbeSizeLimitingTranscodeManager>.Instance);
        StreamState state = StreamStateFor(
            MediaSourceFactory.Create(new StreamLink("https://cdn.test/master.m3u8", "hls", MakoStreams.DefaultDescription), "mako"));

        await sut.StartFfMpeg(state, "out.ts", OriginalArguments, Guid.NewGuid(), TranscodingJobType.Hls, new CancellationTokenSource());

        Assert.Equal("-probesize 20M -i \"in.m3u8\"", recorder.LastCommandLineArguments);
    }

    [Fact]
    public async Task PassesTheOriginalArgumentsUnchangedForAProviderThatNamesNoProbeSize()
    {
        var recorder = new CallRecorder();
        var fake = new FakeTranscodeManager(NullLoggerFactory.Instance, recorder);
        ProviderInfo noLimit = new("other", "Other", "https://other.test", ProbeSize: null, DefaultCategories: new Dictionary<string, string>());
        var sut = new ProbeSizeLimitingTranscodeManager(fake, [MakoProvider.Description, noLimit], NullLogger<ProbeSizeLimitingTranscodeManager>.Instance);
        StreamState state = StreamStateFor(
            MediaSourceFactory.Create(new StreamLink("https://cdn.test/master.m3u8", "hls", MakoStreams.DefaultDescription), "other"));

        await sut.StartFfMpeg(state, "out.ts", OriginalArguments, Guid.NewGuid(), TranscodingJobType.Hls, new CancellationTokenSource());

        Assert.Same(OriginalArguments, recorder.LastCommandLineArguments);
    }

    [Fact]
    public async Task PassesTheOriginalArgumentsUnchangedForAnyOtherStream()
    {
        var recorder = new CallRecorder();
        var fake = new FakeTranscodeManager(NullLoggerFactory.Instance, recorder);
        var sut = new ProbeSizeLimitingTranscodeManager(fake, [MakoProvider.Description], NullLogger<ProbeSizeLimitingTranscodeManager>.Instance);
        StreamState state = StreamStateFor(new MediaSourceInfo { Name = "some-other-plugin" });

        await sut.StartFfMpeg(state, "out.ts", OriginalArguments, Guid.NewGuid(), TranscodingJobType.Hls, new CancellationTokenSource());

        Assert.Same(OriginalArguments, recorder.LastCommandLineArguments);
    }

    [Fact]
    public void DelegatesPingTranscodingJobToTheInnerManager()
    {
        var recorder = new CallRecorder();
        var fake = new FakeTranscodeManager(NullLoggerFactory.Instance, recorder);
        var sut = new ProbeSizeLimitingTranscodeManager(fake, [MakoProvider.Description], NullLogger<ProbeSizeLimitingTranscodeManager>.Instance);

        sut.PingTranscodingJob("session-1", true);

        Assert.Equal("session-1", recorder.LastPingedPlaySessionId);
    }

    [Fact]
    public void DelegatesGetTranscodingJobByPlaySessionIdToTheInnerManager()
    {
        var recorder = new CallRecorder();
        var fake = new FakeTranscodeManager(NullLoggerFactory.Instance, recorder);
        var sut = new ProbeSizeLimitingTranscodeManager(fake, [MakoProvider.Description], NullLogger<ProbeSizeLimitingTranscodeManager>.Instance);

        sut.GetTranscodingJob("session-2");

        Assert.Equal("session-2", recorder.LastRequestedPlaySessionId);
    }

    [Fact]
    public async Task FallsBackToTheOriginalArgumentsAndStillCallsInnerWhenTheStreamCannotBeRecognised()
    {
        var recorder = new CallRecorder();
        var fake = new FakeTranscodeManager(NullLoggerFactory.Instance, recorder);
        var sut = new ProbeSizeLimitingTranscodeManager(fake, [MakoProvider.Description], NullLogger<ProbeSizeLimitingTranscodeManager>.Instance);

        // A null StreamState is not a real Jellyfin call, but it is the simplest honest way to
        // make recognising the stream throw without weakening ArgumentsForThisStream's own
        // try/catch: state.MediaSource raises a NullReferenceException, exactly the kind of
        // failure the fail-open path exists for.
        await sut.StartFfMpeg(null!, "out.ts", OriginalArguments, Guid.NewGuid(), TranscodingJobType.Hls, new CancellationTokenSource());

        Assert.Equal(1, recorder.StartFfMpegCallCount);
        Assert.Same(OriginalArguments, recorder.LastCommandLineArguments);
    }

    [Fact]
    public void DisposingTheDecoratorDisposesADisposableInner()
    {
        var recorder = new CallRecorder();
        var fake = new FakeTranscodeManager(NullLoggerFactory.Instance, recorder);
        var sut = new ProbeSizeLimitingTranscodeManager(fake, [MakoProvider.Description], NullLogger<ProbeSizeLimitingTranscodeManager>.Instance);

        sut.Dispose();

        Assert.True(recorder.WasDisposed);
    }

    [Fact]
    public void DisposingTwiceDisposesTheInnerOnlyOnce()
    {
        var recorder = new CallRecorder();
        var fake = new FakeTranscodeManager(NullLoggerFactory.Instance, recorder);
        var sut = new ProbeSizeLimitingTranscodeManager(fake, [MakoProvider.Description], NullLogger<ProbeSizeLimitingTranscodeManager>.Instance);

        sut.Dispose();
        sut.Dispose();

        Assert.Equal(1, recorder.DisposeCallCount);
    }

    // Constructs the same way Jellyfin does: MediaSourceManager and the outer transcode
    // manager are irrelevant to picking a probe size, so they are left null. Verified against
    // the real StreamState constructor in Jellyfin.Controller 12.1.0 by reflection: it stores
    // both without dereferencing them, and MediaSource is a plain settable property.
    private static StreamState StreamStateFor(MediaSourceInfo mediaSource) =>
        new(mediaSourceManager: null!, TranscodingJobType.Hls, transcodeManager: null!) { MediaSource = mediaSource };
}
