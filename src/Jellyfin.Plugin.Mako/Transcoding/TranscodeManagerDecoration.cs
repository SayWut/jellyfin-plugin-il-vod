using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Mako.Transcoding;

/// <summary>
/// Replaces Jellyfin's registered <see cref="ITranscodeManager"/> with one wrapped in
/// <see cref="ProbeSizeLimitingTranscodeManager"/>, so ffmpeg's probe size can be limited for
/// mako's own streams. Every check here fails safe: if Core's registration is not the shape
/// this expects, the container is left exactly as it was and the server runs as before.
/// </summary>
public static class TranscodeManagerDecoration
{
    /// <summary>
    /// Finds the last <see cref="ITranscodeManager"/> registration and, if it was registered by
    /// implementation type (as Jellyfin Core registers it), replaces it with one that wraps the
    /// original behind <see cref="ProbeSizeLimitingTranscodeManager"/>, preserving the original
    /// registration's lifetime. Does nothing when there is no such registration, or it was
    /// registered by instance or factory instead of by type — there is no implementation type to
    /// construct through DI in that case, so the collection is left untouched.
    /// </summary>
    /// <returns>
    /// True when the wrapper was installed. No logger exists yet at this point in server
    /// startup, so the result is recorded in <see cref="ProbeSizeLimiterStatus.IsInstalled"/>
    /// for code that runs later — and does have a logger — to report.
    /// </returns>
    public static bool Apply(IServiceCollection services)
    {
        ServiceDescriptor? registration = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(ITranscodeManager));
        if (registration?.ImplementationType is not Type implementationType)
        {
            ProbeSizeLimiterStatus.IsInstalled = false;
            return false;
        }

        services.Remove(registration);
        services.Add(ServiceDescriptor.Describe(
            typeof(ITranscodeManager),
            provider => CreateDecorator(provider, implementationType),
            registration.Lifetime));

        ProbeSizeLimiterStatus.IsInstalled = true;
        return true;
    }

    private static ProbeSizeLimitingTranscodeManager CreateDecorator(IServiceProvider provider, Type implementationType) =>
        new(
            (ITranscodeManager)ActivatorUtilities.CreateInstance(provider, implementationType),
            provider.GetRequiredService<ILogger<ProbeSizeLimitingTranscodeManager>>());
}
