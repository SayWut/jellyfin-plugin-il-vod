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
    public static void Apply(IServiceCollection services)
    {
        ServiceDescriptor? registration = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(ITranscodeManager));
        if (registration?.ImplementationType is not Type implementationType)
        {
            return;
        }

        services.Remove(registration);
        services.AddSingleton<ITranscodeManager>(provider => new ProbeSizeLimitingTranscodeManager(
            (ITranscodeManager)ActivatorUtilities.CreateInstance(provider, implementationType),
            provider.GetRequiredService<ILogger<ProbeSizeLimitingTranscodeManager>>()));
    }
}
