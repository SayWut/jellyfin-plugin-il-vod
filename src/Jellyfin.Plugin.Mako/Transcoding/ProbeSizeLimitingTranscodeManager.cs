using Jellyfin.Plugin.Mako.Browsing;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Mako.Transcoding;

/// <summary>
/// Wraps Jellyfin's real <see cref="ITranscodeManager"/> so ffmpeg's probe size can be limited
/// for mako's own streams without touching the server-wide setting every other stream relies
/// on. Every member delegates to <paramref name="inner"/> unchanged except
/// <see cref="StartFfMpeg"/>, and every stream but mako's passes through that unchanged too.
/// </summary>
/// <remarks>
/// This class sits in front of every stream the server plays, so it must never be the reason
/// one of them fails: <see cref="StartFfMpeg"/> catches broadly and falls back to the original
/// arguments on any failure to recognise or rewrite them. That breadth is deliberate boundary
/// code, not a missed exception type.
/// </remarks>
public sealed class ProbeSizeLimitingTranscodeManager : ITranscodeManager, IDisposable
{
    private readonly ITranscodeManager _inner;
    private readonly ILogger<ProbeSizeLimitingTranscodeManager> _logger;

    public ProbeSizeLimitingTranscodeManager(ITranscodeManager inner, ILogger<ProbeSizeLimitingTranscodeManager> logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public TranscodingJob? GetTranscodingJob(string playSessionId) => _inner.GetTranscodingJob(playSessionId);

    public TranscodingJob? GetTranscodingJob(string path, TranscodingJobType type) => _inner.GetTranscodingJob(path, type);

    public void PingTranscodingJob(string playSessionId, bool? isUserPaused) =>
        _inner.PingTranscodingJob(playSessionId, isUserPaused);

    public Task KillTranscodingJobs(string deviceId, string? playSessionId, Func<string, bool> deleteFiles) =>
        _inner.KillTranscodingJobs(deviceId, playSessionId, deleteFiles);

    public void ReportTranscodingProgress(
        TranscodingJob job,
        StreamState state,
        TimeSpan? transcodingPosition,
        float? framerate,
        double? percentComplete,
        long? bytesTranscoded,
        int? bitRate) =>
        _inner.ReportTranscodingProgress(job, state, transcodingPosition, framerate, percentComplete, bytesTranscoded, bitRate);

    public Task<TranscodingJob> StartFfMpeg(
        StreamState state,
        string outputPath,
        string commandLineArguments,
        Guid userId,
        TranscodingJobType transcodingJobType,
        CancellationTokenSource cancellationTokenSource,
        string? workingDirectory = null)
    {
        string argumentsToUse = ArgumentsForThisStream(state, commandLineArguments);
        return _inner.StartFfMpeg(state, outputPath, argumentsToUse, userId, transcodingJobType, cancellationTokenSource, workingDirectory);
    }

    public TranscodingJob? OnTranscodeBeginRequest(string path, TranscodingJobType type) =>
        _inner.OnTranscodeBeginRequest(path, type);

    public void OnTranscodeEndRequest(TranscodingJob job) => _inner.OnTranscodeEndRequest(job);

    public ValueTask<IDisposable> LockAsync(string outputPath, CancellationToken cancellationToken) =>
        _inner.LockAsync(outputPath, cancellationToken);

    public void Dispose()
    {
        if (_inner is IDisposable disposableInner)
        {
            disposableInner.Dispose();
        }
    }

    private string ArgumentsForThisStream(StreamState state, string commandLineArguments)
    {
        try
        {
            if (!MediaSourceFactory.IsMakoSource(state.MediaSource))
            {
                return commandLineArguments;
            }

            _logger.LogInformation("mako: limiting ffmpeg probe size to {ProbeSize} for this stream", ProbeSizeLimiter.MakoProbeSize);
            return ProbeSizeLimiter.Limit(commandLineArguments);
        }
        catch (Exception ex)
        {
            // Fail open: this class wraps a component every stream on the server depends on, so
            // no path through it may prevent a non-mako stream from playing. Whatever went
            // wrong, send ffmpeg exactly what Jellyfin built.
            _logger.LogWarning(ex, "mako: could not check or limit the ffmpeg probe size; using Jellyfin's original arguments.");
            return commandLineArguments;
        }
    }
}
