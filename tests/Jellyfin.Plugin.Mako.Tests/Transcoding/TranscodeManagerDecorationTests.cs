using Jellyfin.Plugin.Mako.Transcoding;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.Mako.Tests.Transcoding;

public class TranscodeManagerDecorationTests
{
    [Fact]
    public void WrapsAContainerRegisteredTranscodeManagerAndStillReachesIt()
    {
        var recorder = new CallRecorder();
        ServiceCollection services = ServicesWithLogging();
        services.AddSingleton(recorder);
        services.AddSingleton<ITranscodeManager, FakeTranscodeManager>();

        TranscodeManagerDecoration.Apply(services);

        using ServiceProvider provider = services.BuildServiceProvider();
        ITranscodeManager resolved = provider.GetRequiredService<ITranscodeManager>();

        Assert.IsType<ProbeSizeLimitingTranscodeManager>(resolved);

        resolved.PingTranscodingJob("session-42", null);

        Assert.Equal("session-42", recorder.LastPingedPlaySessionId);
    }

    [Fact]
    public void DoesNothingOnAnEmptyServiceCollection()
    {
        var services = new ServiceCollection();

        Exception? exception = Record.Exception(() => TranscodeManagerDecoration.Apply(services));

        Assert.Null(exception);
        Assert.Empty(services);
    }

    [Fact]
    public void LeavesARegistrationByInstanceUntouched()
    {
        var services = new ServiceCollection();
        var instance = new FakeTranscodeManager(NullLoggerFactory.Instance, new CallRecorder());
        services.AddSingleton<ITranscodeManager>(instance);

        TranscodeManagerDecoration.Apply(services);

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Same(instance, provider.GetRequiredService<ITranscodeManager>());
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
}
