using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IlVod.Transcoding;

/// <summary>
/// Replaces Jellyfin's registered <see cref="ITranscodeManager"/> with one wrapped in
/// <see cref="ProbeSizeLimitingTranscodeManager"/>, so ffmpeg's probe size can be limited for
/// mako's own streams. Every check here fails safe: if Core has no such registration at all,
/// the container is left exactly as it was and the server runs as before.
/// </summary>
public static class TranscodeManagerDecoration
{
    /// <summary>
    /// Finds the last <see cref="ITranscodeManager"/> registration — however it was registered,
    /// by type (as Jellyfin Core registers it), by factory or by instance (as another decorating
    /// plugin's own wrapper would leave it) — and replaces it with one that wraps the original
    /// behind <see cref="ProbeSizeLimitingTranscodeManager"/>, preserving the original
    /// registration's lifetime. Does nothing when there is no such registration.
    /// </summary>
    /// <returns>
    /// True when the wrapper was installed. No logger exists yet at this point in server
    /// startup, so the result is recorded in <see cref="ProbeSizeLimiterStatus.IsInstalled"/>
    /// for code that runs later — and does have a logger — to report.
    /// </returns>
    public static bool Apply(IServiceCollection services)
    {
        ServiceDescriptor? registration = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(ITranscodeManager));
        if (registration is null)
        {
            ProbeSizeLimiterStatus.IsInstalled = false;
            return false;
        }

        (Func<IServiceProvider, ITranscodeManager> buildInner, bool ownsInner) = InnerFactoryOf(registration);
        services.Remove(registration);
        services.Add(ServiceDescriptor.Describe(
            typeof(ITranscodeManager),
            provider => CreateDecorator(provider, buildInner, ownsInner),
            registration.Lifetime));

        ProbeSizeLimiterStatus.IsInstalled = true;
        return true;
    }

    // Builds the inner manager the same way regardless of how Core (or an earlier decorator)
    // registered it, so a second decorating plugin can always wrap what this one leaves behind
    // — and decides who owns the result. An instance registration is never disposed by .NET DI
    // itself, so this decorator must not dispose it either; a factory or type registration
    // builds a fresh, container-owned instance, which this decorator disposing on the
    // container's behalf is correct.
    private static (Func<IServiceProvider, ITranscodeManager> Build, bool OwnsInner) InnerFactoryOf(ServiceDescriptor registration)
    {
        if (registration.ImplementationInstance is ITranscodeManager instance)
        {
            return (_ => instance, false);
        }

        if (registration.ImplementationFactory is { } factory)
        {
            return (provider => (ITranscodeManager)factory(provider), true);
        }

        Type implementationType = registration.ImplementationType!;
        return (provider => (ITranscodeManager)ActivatorUtilities.CreateInstance(provider, implementationType), true);
    }

    private static ProbeSizeLimitingTranscodeManager CreateDecorator(IServiceProvider provider, Func<IServiceProvider, ITranscodeManager> buildInner, bool ownsInner) =>
        new(
            buildInner(provider),
            provider.GetRequiredService<ILogger<ProbeSizeLimitingTranscodeManager>>(),
            ownsInner);
}
