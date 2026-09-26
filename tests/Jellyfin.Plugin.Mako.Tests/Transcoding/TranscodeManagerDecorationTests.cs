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
    public void WrapsARegistrationByInstance()
    {
        var recorder = new CallRecorder();
        ServiceCollection services = ServicesWithLogging();
        var instance = new FakeTranscodeManager(NullLoggerFactory.Instance, recorder);
        services.AddSingleton<ITranscodeManager>(instance);

        bool installed = TranscodeManagerDecoration.Apply(services);

        Assert.True(installed);
        Assert.True(ProbeSizeLimiterStatus.IsInstalled);

        using ServiceProvider provider = services.BuildServiceProvider();
        ITranscodeManager resolved = provider.GetRequiredService<ITranscodeManager>();

        Assert.IsType<ProbeSizeLimitingTranscodeManager>(resolved);

        resolved.PingTranscodingJob("session-instance", null);

        Assert.Equal("session-instance", recorder.LastPingedPlaySessionId);
    }

    [Fact]
    public void WrapsARegistrationByFactory()
    {
        var recorder = new CallRecorder();
        ServiceCollection services = ServicesWithLogging();
        services.AddSingleton<ITranscodeManager>(_ => new FakeTranscodeManager(NullLoggerFactory.Instance, recorder));

        bool installed = TranscodeManagerDecoration.Apply(services);

        Assert.True(installed);
        Assert.True(ProbeSizeLimiterStatus.IsInstalled);

        using ServiceProvider provider = services.BuildServiceProvider();
        ITranscodeManager resolved = provider.GetRequiredService<ITranscodeManager>();

        Assert.IsType<ProbeSizeLimitingTranscodeManager>(resolved);

        resolved.PingTranscodingJob("session-factory", null);

        Assert.Equal("session-factory", recorder.LastPingedPlaySessionId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NestsInsideAnotherDecoratorInEitherOrder(bool makoDecoratesFirst)
    {
        var recorder = new CallRecorder();
        ServiceCollection services = ServicesWithLogging();
        services.AddSingleton(recorder);
        services.AddSingleton<ITranscodeManager, FakeTranscodeManager>();

        if (makoDecoratesFirst)
        {
            TranscodeManagerDecoration.Apply(services);
            ApplyForwardingDecoration(services);
        }
        else
        {
            ApplyForwardingDecoration(services);
            TranscodeManagerDecoration.Apply(services);
        }

        using ServiceProvider provider = services.BuildServiceProvider();
        ITranscodeManager resolved = provider.GetRequiredService<ITranscodeManager>();

        resolved.PingTranscodingJob("session-nested", null);

        Assert.Equal("session-nested", recorder.LastPingedPlaySessionId);
    }

    // Stands in for a second, independent decorating plugin: wraps the last ITranscodeManager
    // registration behind ForwardingWrapper the same way TranscodeManagerDecoration.Apply wraps
    // it behind ProbeSizeLimitingTranscodeManager, so the test can nest the two in either order.
    private static void ApplyForwardingDecoration(IServiceCollection services)
    {
        ServiceDescriptor registration = services.Last(descriptor => descriptor.ServiceType == typeof(ITranscodeManager));
        Func<IServiceProvider, ITranscodeManager> buildInner = InnerFactoryOf(registration);

        services.Remove(registration);
        services.Add(ServiceDescriptor.Describe(
            typeof(ITranscodeManager),
            provider => new ForwardingWrapper(buildInner(provider)),
            registration.Lifetime));
    }

    private static Func<IServiceProvider, ITranscodeManager> InnerFactoryOf(ServiceDescriptor registration)
    {
        if (registration.ImplementationInstance is ITranscodeManager instance)
        {
            return _ => instance;
        }

        if (registration.ImplementationFactory is { } factory)
        {
            return provider => (ITranscodeManager)factory(provider);
        }

        Type implementationType = registration.ImplementationType!;
        return provider => (ITranscodeManager)ActivatorUtilities.CreateInstance(provider, implementationType);
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

    // Stands in for a second decorating plugin's own wrapper: forwards every member to
    // whatever it wraps, unchanged, so nesting it around or inside
    // ProbeSizeLimitingTranscodeManager proves both directions actually delegate.
    private sealed class ForwardingWrapper : ITranscodeManager
    {
        private readonly ITranscodeManager _inner;

        public ForwardingWrapper(ITranscodeManager inner) => _inner = inner;

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
            string? workingDirectory = null) =>
            _inner.StartFfMpeg(state, outputPath, commandLineArguments, userId, transcodingJobType, cancellationTokenSource, workingDirectory);

        public TranscodingJob? OnTranscodeBeginRequest(string path, TranscodingJobType type) =>
            _inner.OnTranscodeBeginRequest(path, type);

        public void OnTranscodeEndRequest(TranscodingJob job) => _inner.OnTranscodeEndRequest(job);

        public ValueTask<IDisposable> LockAsync(string outputPath, CancellationToken cancellationToken) =>
            _inner.LockAsync(outputPath, cancellationToken);
    }
}
