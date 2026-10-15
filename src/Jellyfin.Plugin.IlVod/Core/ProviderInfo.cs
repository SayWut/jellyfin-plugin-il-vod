namespace Jellyfin.Plugin.IlVod.Core;

/// <summary>
/// What the channel needs to know about one content provider, apart from its content: how to name
/// it, how its categories read in the shared vocabulary, and how its streams must be handled at
/// play time. Registered on its own so the settings page and the probe-size limiter can read it
/// without building the provider's network graph.
/// </summary>
/// <param name="Id">
/// A short, stable, lower-case id (<c>[a-z0-9]+</c>). It prefixes every show and episode id the
/// provider's content gets inside the channel, so changing it makes Jellyfin recreate them all.
/// </param>
/// <param name="DisplayName">The name shown on the settings page.</param>
/// <param name="HomePageUrl">The provider's own VOD site.</param>
/// <param name="ProbeSize">
/// The ffmpeg <c>-probesize</c> this provider's streams must be limited to, or <c>null</c> to leave
/// Jellyfin's own setting untouched (see <c>Transcoding/ProbeSizeLimitingTranscodeManager</c>).
/// </param>
/// <param name="DefaultCategories">
/// The provider's category name → the <see cref="SharedCategories"/> name it means. A name missing
/// here is shown as the provider spells it; the settings page can override any entry.
/// </param>
public sealed record ProviderInfo(
    string Id,
    string DisplayName,
    string HomePageUrl,
    string? ProbeSize,
    IReadOnlyDictionary<string, string> DefaultCategories);
