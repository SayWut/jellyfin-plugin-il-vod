using Jellyfin.Plugin.Mako.Transcoding;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.Mako.Tests.Transcoding;

[Collection(ProbeSizeLimiterStatusCollection.Name)]
public class TranscodeManagerDecorationTests
{
    [Fact]
    public void WrapsAContainerRegisteredTranscodeManagerAndStillReachesIt()
    {
        var recorder = new CallRecorder();
        ServiceCollection services = ServicesWithLogging();
        services.AddSingleton(recorder);
        services.AddSingleton<ITranscodeManager, FakeTranscodeManager>();

        bool installed = TranscodeManagerDecoration.Apply(services);

        Assert.True(installed);
        Assert.True(ProbeSizeLimiterStatus.IsInstalled);

        using ServiceProvider provider = services.BuildServiceProvider();
        ITranscodeManager resolved = provider.GetRequiredService<ITranscodeManager>();

        Assert.IsType<ProbeSizeLimitingTranscodeManager>(resolved);

        resolved.PingTranscodingJob("session-42", null);

        Assert.Equal("session-42", recorder.LastPingedPlaySessionId);
    }

    [Fact]
    public void PreservesTheOriginalRegistrationsLifetime()
    {
        var services = ServicesWithLogging();
        services.AddSingleton(new CallRecorder());
        services.AddScoped<ITranscodeManager, FakeTranscodeManager>();

        TranscodeManagerDecoration.Apply(services);

        ServiceDescriptor wrapped = Assert.Single(services, d => d.ServiceType == typeof(ITranscodeManager));
        Assert.Equal(ServiceLifetime.Scoped, wrapped.Lifetime);
    }

    [Fact]
    public void DoesNothingOnAnEmptyServiceCollection()
    {
        var services = new ServiceCollection();

        bool installed = true;
        Exception? exception = Record.Exception(() => installed = TranscodeManagerDecoration.Apply(services));

        Assert.Null(exception);
        Assert.False(installed);
        Assert.False(ProbeSizeLimiterStatus.IsInstalled);
        Assert.Empty(services);
    }

    [Fact]
    public void LeavesARegistrationByInstanceUntouched()
    {
        var services = new ServiceCollection();
        var instance = new FakeTranscodeManager(NullLoggerFactory.Instance, new CallRecorder());
        services.AddSingleton<ITranscodeManager>(instance);

        bool installed = TranscodeManagerDecoration.Apply(services);

        Assert.False(installed);
        Assert.False(ProbeSizeLimiterStatus.IsInstalled);

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Same(instance, provider.GetRequiredService<ITranscodeManager>());
    }

    [Fact]
    public void LeavesARegistrationByFactoryUntouched()
    {
        var services = new ServiceCollection();
        var recorder = new CallRecorder();
        services.AddSingleton<ITranscodeManager>(_ => new FakeTranscodeManager(NullLoggerFactory.Instance, recorder));

        bool installed = TranscodeManagerDecoration.Apply(services);

        Assert.False(installed);
        Assert.False(ProbeSizeLimiterStatus.IsInstalled);

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.IsType<FakeTranscodeManager>(provider.GetRequiredService<ITranscodeManager>());
    }

    [Fact]
    public void WithTwoTypeRegistrationsWrapsOnlyTheLastOne()
    {
        var recorder = new CallRecorder();
        ServiceCollection services = ServicesWithLogging();
        services.AddSingleton(recorder);
        services.AddSingleton<ITranscodeManager, NeverCalledTranscodeManager>();
        services.AddSingleton<ITranscodeManager, FakeTranscodeManager>();

        TranscodeManagerDecoration.Apply(services);

        using ServiceProvider provider = services.BuildServiceProvider();
        ITranscodeManager resolved = provider.GetRequiredService<ITranscodeManager>();

        // Would throw if the FIRST registration (NeverCalledTranscodeManager) had been the one
        // wrapped instead of the last (FakeTranscodeManager).
        resolved.PingTranscodingJob("session-1", null);

        Assert.Equal("session-1", recorder.LastPingedPlaySessionId);
    }

    [Fact]
    public void DisposingTheContainerDisposesTheContainerConstructedInnerManager()
    {
        var recorder = new CallRecorder();
        ServiceCollection services = ServicesWithLogging();
        services.AddSingleton(recorder);
        services.AddSingleton<ITranscodeManager, FakeTranscodeManager>();

        TranscodeManagerDecoration.Apply(services);

        ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITranscodeManager>();

        provider.Dispose();

        Assert.True(recorder.WasDisposed);
    }

    // Registers just enough logging plumbing for ActivatorUtilities to satisfy
    // ProbeSizeLimitingTranscodeManager's and FakeTranscodeManager's ILogger*/ILoggerFactory
    // constructor dependencies, without depending on the full Microsoft.Extensions.Logging
    // package (only the Abstractions types the plugin already uses are needed).
    private static ServiceCollection ServicesWithLogging()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        return services;
    }

    // Proves which of two type registrations Apply picks: every member throws, so the test
    // fails loudly if this one is ever the one actually wrapped and called.
    private sealed class NeverCalledTranscodeManager : ITranscodeManager
    {
        public TranscodingJob? GetTranscodingJob(string playSessionId) => throw Unexpected();

        public TranscodingJob? GetTranscodingJob(string path, TranscodingJobType type) => throw Unexpected();

        public void PingTranscodingJob(string playSessionId, bool? isUserPaused) => throw Unexpected();

        public Task KillTranscodingJobs(string deviceId, string? playSessionId, Func<string, bool> deleteFiles) => throw Unexpected();

        public void ReportTranscodingProgress(
            TranscodingJob job,
            StreamState state,
            TimeSpan? transcodingPosition,
            float? framerate,
            double? percentComplete,
            long? bytesTranscoded,
            int? bitRate) => throw Unexpected();

        public Task<TranscodingJob> StartFfMpeg(
            StreamState state,
            string outputPath,
            string commandLineArguments,
            Guid userId,
            TranscodingJobType transcodingJobType,
            CancellationTokenSource cancellationTokenSource,
            string? workingDirectory = null) => throw Unexpected();

        public TranscodingJob? OnTranscodeBeginRequest(string path, TranscodingJobType type) => throw Unexpected();

        public void OnTranscodeEndRequest(TranscodingJob job) => throw Unexpected();

        public ValueTask<IDisposable> LockAsync(string outputPath, CancellationToken cancellationToken) => throw Unexpected();

        private static InvalidOperationException Unexpected() =>
            new("NeverCalledTranscodeManager was called; it should have been superseded by the later registration.");
    }
}
