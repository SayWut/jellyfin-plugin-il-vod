using Jellyfin.Plugin.IlVod.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IlVod.Hls;

/// <summary>
/// Reads a stream's playlists at play time, for what they tell Jellyfin about the stream. Both reads
/// are best-effort: the stream's URL is right whether or not its playlists could be read just now,
/// so a failure leaves the stream undescribed (and logged, its query never shown — N3) rather than
/// failing playback. Cancellation is never swallowed.
/// </summary>
public sealed class HlsPlaylistReader
{
    private readonly Func<string, CancellationToken, Task<string>> _getText;
    private readonly string _providerId;
    private readonly ILogger _logger;

    /// <param name="getText">The provider's own GET, so its failures and timeouts are the provider's.</param>
    /// <param name="providerId">Prefixes every warning, as the provider's own log lines are.</param>
    public HlsPlaylistReader(Func<string, CancellationToken, Task<string>> getText, string providerId, ILogger logger)
    {
        _getText = getText;
        _providerId = providerId;
        _logger = logger;
    }

    /// <summary>The master playlist's text; <c>null</c> when it could not be read.</summary>
    public async Task<string?> ReadMasterAsync(string masterUrl, CancellationToken cancellationToken)
    {
        try
        {
            return await _getText(masterUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                "{ProviderId}: could not describe the master playlist at {HostAndPath} ({Reason}); using the default stream description",
                _providerId,
                SafeUrl.Redacted(masterUrl),
                exception.GetType().Name);
            return null;
        }
    }

    /// <summary>
    /// Jellyfin treats a source with no run time as a live stream — players join late and cannot
    /// rewind — so a stream of unknown length is measured: its variant playlist lists every
    /// segment, and their sum is the length.
    /// </summary>
    /// <returns><c>null</c> when there is no variant to measure, or it could not be read.</returns>
    public async Task<TimeSpan?> MeasureLengthAsync(string? variantUrl, CancellationToken cancellationToken)
    {
        if (variantUrl is null)
        {
            return null;
        }

        try
        {
            return MediaPlaylist.TotalDuration(await _getText(variantUrl, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                "{ProviderId}: could not measure the length from {HostAndPath} ({Reason}); Jellyfin will treat the stream as live",
                _providerId,
                SafeUrl.Redacted(variantUrl),
                exception.GetType().Name);
            return null;
        }
    }

    /// <summary>The master playlist's best variant as an absolute URL; <c>null</c> when it names none.</summary>
    public static string? BestVariantUrlOf(string masterUrl, string masterPlaylistText) =>
        MasterPlaylist.BestVariantUri(masterPlaylistText) is string variantUri
            && Uri.TryCreate(new Uri(masterUrl), variantUri, out Uri? variantUrl)
                ? variantUrl.AbsoluteUri
                : null;
}
