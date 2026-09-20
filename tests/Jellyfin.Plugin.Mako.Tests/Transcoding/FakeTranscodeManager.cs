using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.Mako.Tests.Transcoding;

/// <summary>
/// Hand-written stand-in for Jellyfin's real transcode manager (no mocking library, per the
/// task brief). Takes an <see cref="ILoggerFactory"/> dependency so a test can prove
/// <c>ActivatorUtilities</c> constructed it through DI rather than by hand, and records every
/// call into a shared <see cref="CallRecorder"/> the test can inspect afterwards.
/// </summary>
public sealed class FakeTranscodeManager : ITranscodeManager, IDisposable
{
    private readonly CallRecorder _recorder;

    public FakeTranscodeManager(ILoggerFactory loggerFactory, CallRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _recorder = recorder;
    }

    public TranscodingJob? GetTranscodingJob(string playSessionId)
    {
        _recorder.LastRequestedPlaySessionId = playSessionId;
        return null;
    }

    public TranscodingJob? GetTranscodingJob(string path, TranscodingJobType type) => null;

    public void PingTranscodingJob(string playSessionId, bool? isUserPaused) =>
        _recorder.LastPingedPlaySessionId = playSessionId;

    public Task KillTranscodingJobs(string deviceId, string? playSessionId, Func<string, bool> deleteFiles) =>
        Task.CompletedTask;

    public void ReportTranscodingProgress(
        TranscodingJob job,
        StreamState state,
        TimeSpan? transcodingPosition,
        float? framerate,
        double? percentComplete,
        long? bytesTranscoded,
        int? bitRate)
    {
    }

    public Task<TranscodingJob> StartFfMpeg(
        StreamState state,
        string outputPath,
        string commandLineArguments,
        Guid userId,
        TranscodingJobType transcodingJobType,
        CancellationTokenSource cancellationTokenSource,
        string? workingDirectory = null)
    {
        _recorder.LastCommandLineArguments = commandLineArguments;
        return Task.FromResult(new TranscodingJob(NullLogger<TranscodingJob>.Instance));
    }

    public TranscodingJob? OnTranscodeBeginRequest(string path, TranscodingJobType type) => null;

    public void OnTranscodeEndRequest(TranscodingJob job)
    {
    }

    public ValueTask<IDisposable> LockAsync(string outputPath, CancellationToken cancellationToken) =>
        new(NoopDisposable.Instance);

    public void Dispose() => _recorder.WasDisposed = true;

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
