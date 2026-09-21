using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.Mako.Model;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Mako.MakoApi;

/// <summary>
/// Resolves a playable stream at the moment play is pressed (spec §5.2): fetch and decrypt the
/// episode's playlist, mint an entitlement token when the item is protected, and describe the
/// stream from its HLS master playlist. Nothing here is cached — task B8's <c>MakoPageSource</c>
/// is where the 5-minute stream-link cache (N2) lives, since a signed URL must never sit on disk
/// (N3) and this class has no disk of its own to sit on.
/// </summary>
public sealed class StreamResolver
{
    private const string HlsContainer = "hls";
    private const string TokenContentType = "application/json; charset=utf-8";
    private const string EntitlementType = "ngt";

    // Token request body field names, in the exact order the request body's JSON must carry them.
    private const string AppIdField = "da";
    private const string DeviceIdField = "du";
    private const string EntitlementTypeField = "et";
    private const string AppVersionField = "na";
    private const string CdnField = "rv";
    private const string MediaUrlField = "lp";
    private const string VcmIdField = "dv";

    private const string MediaProperty = "media";
    private const string CdnWeightProperty = "cdnLB";
    private const string MediaUrlProperty = "url";
    private const string MediaCdnProperty = "cdn";
    private const string VideoDetailsProperty = "videoDetails";
    private const string ExtraParamsProperty = "extraParams";
    private const string ProtectedContentProperty = "protectedContent";
    private const string TicketsProperty = "tickets";
    private const string TicketProperty = "ticket";
    private const string CaseIdProperty = "caseId";

    private readonly IMakoHttp _http;
    private readonly MakoConfigLoader _config;
    private readonly DeviceIdentity _device;
    private readonly Func<string> _readAppVersion;
    private readonly ILogger<StreamResolver> _logger;

    public StreamResolver(
        IMakoHttp http,
        MakoConfigLoader config,
        DeviceIdentity device,
        Func<string> readAppVersion,
        ILogger<StreamResolver> logger)
    {
        _http = http;
        _config = config;
        _device = device;
        _readAppVersion = readAppVersion;
        _logger = logger;
    }

    public async Task<StreamLink> ResolveAsync(EpisodeKey episode, CancellationToken cancellationToken)
    {
        MakoConfig config = await _config.LoadAsync(cancellationToken).ConfigureAwait(false);
        (config, string playlistJson) = await FetchPlaylistAsync(config, episode, cancellationToken).ConfigureAwait(false);

        PlaylistMedia media = ParseBestMedia(playlistJson);

        string finalUrl = media.Protected
            ? await AuthorizeAsync(config, episode, media, cancellationToken).ConfigureAwait(false)
            : media.Url;

        StreamDescription description = await DescribeMasterPlaylistAsync(finalUrl, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "mako: resolved stream for {VcmId} (protected: {Protected}, cdn: {Cdn})",
            episode.VcmId,
            media.Protected,
            media.Cdn);

        return new StreamLink(finalUrl, HlsContainer, description);
    }

    /// <summary>
    /// Fetches the playlist once and decrypts it with <paramref name="config"/>'s keys. A key mako
    /// has since rotated past what a (up to 24h stale) cached config still holds throws
    /// <see cref="PayloadDecryptException"/> from every key <c>DecryptWithAnyKey</c> tried; that is
    /// answered with exactly one forced config refetch and one more decrypt attempt against the
    /// same already-downloaded body, never a second playlist request. A second decrypt failure is
    /// not caught here, so it propagates rather than retrying forever.
    /// </summary>
    private async Task<(MakoConfig Config, string PlaylistJson)> FetchPlaylistAsync(
        MakoConfig config,
        EpisodeKey episode,
        CancellationToken cancellationToken)
    {
        string playlistUrl = MakoUrls.Playlist(config.PlaylistUrlTemplate, episode.VcmId, episode.ChannelId, episode.GalleryChannelId);
        string encryptedPlaylist = await _http.GetTextAsync(playlistUrl, cancellationToken).ConfigureAwait(false);

        try
        {
            return (config, PayloadCipher.DecryptWithAnyKey(config.PlaylistKeys, encryptedPlaylist, "playlist"));
        }
        catch (PayloadDecryptException)
        {
            _config.Forget();
            MakoConfig freshConfig = await _config.LoadAsync(cancellationToken).ConfigureAwait(false);
            return (freshConfig, PayloadCipher.DecryptWithAnyKey(freshConfig.PlaylistKeys, encryptedPlaylist, "playlist"));
        }
    }

    /// <exception cref="MakoRequestException">The playlist's <c>media</c> array is missing or empty.</exception>
    private static PlaylistMedia ParseBestMedia(string playlistJson)
    {
        using JsonDocument document = JsonDocument.Parse(playlistJson);
        JsonElement root = document.RootElement;

        if (!TryGetArray(root, MediaProperty, out JsonElement mediaEntries) || mediaEntries.GetArrayLength() == 0)
        {
            throw new MakoRequestException("playlist has no media");
        }

        JsonElement bestMedia = HighestWeightMedia(mediaEntries);
        return new PlaylistMedia(
            Url: TryGetString(bestMedia, MediaUrlProperty) ?? string.Empty,
            Cdn: TryGetString(bestMedia, MediaCdnProperty) ?? string.Empty,
            Protected: IsProtectedContent(root));
    }

    /// <summary>Ties keep the first entry, since only a strictly higher weight ever replaces it.</summary>
    private static JsonElement HighestWeightMedia(JsonElement mediaEntries)
    {
        JsonElement best = mediaEntries[0];
        int bestWeight = CdnWeightOf(best);

        foreach (JsonElement candidate in mediaEntries.EnumerateArray().Skip(1))
        {
            int candidateWeight = CdnWeightOf(candidate);
            if (candidateWeight > bestWeight)
            {
                best = candidate;
                bestWeight = candidateWeight;
            }
        }

        return best;
    }

    /// <summary><c>cdnLB</c> is a JSON string holding a weight; an unparseable one counts as 0.</summary>
    private static int CdnWeightOf(JsonElement media) =>
        TryGetString(media, CdnWeightProperty) is string weightText
        && int.TryParse(weightText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int weight)
            ? weight
            : 0;

    private static bool IsProtectedContent(JsonElement root) =>
        TryGetObject(root, VideoDetailsProperty, out JsonElement videoDetails)
        && TryGetObject(videoDetails, ExtraParamsProperty, out JsonElement extraParams)
        && extraParams.ValueKind == JsonValueKind.Object
        && extraParams.TryGetProperty(ProtectedContentProperty, out JsonElement protectedFlag)
        && protectedFlag.ValueKind == JsonValueKind.True;

    private async Task<string> AuthorizeAsync(MakoConfig config, EpisodeKey episode, PlaylistMedia media, CancellationToken cancellationToken)
    {
        string requestBody = BuildTokenRequestBody(config, episode, media);
        string encryptedRequestBody = PayloadCipher.Encrypt(config.TokenKey, requestBody);

        string encryptedResponse = await _http
            .PostTextAsync(config.TokenUrl, encryptedRequestBody, TokenContentType, cancellationToken)
            .ConfigureAwait(false);
        string tokenJson = PayloadCipher.Decrypt(config.TokenKey, encryptedResponse);

        string ticket = ExtractTicketOrThrow(tokenJson, episode.VcmId);
        return AppendTicket(media.Url, ticket);
    }

    /// <summary>Builds the token request body with its 7 fields in the exact order mako expects, and no <c>dac</c>.</summary>
    private string BuildTokenRequestBody(MakoConfig config, EpisodeKey episode, PlaylistMedia media)
    {
        JsonObject body = new()
        {
            [AppIdField] = config.AppId,
            [DeviceIdField] = _device.Get(),
            [EntitlementTypeField] = EntitlementType,
            [AppVersionField] = _readAppVersion(),
            [CdnField] = media.Cdn,
            [MediaUrlField] = media.Url,
            [VcmIdField] = episode.VcmId,
        };
        return body.ToJsonString();
    }

    /// <exception cref="EntitlementDeniedException">The token response carries no ticket (N5).</exception>
    private static string ExtractTicketOrThrow(string tokenJson, string episodeId)
    {
        using JsonDocument document = JsonDocument.Parse(tokenJson);
        JsonElement root = document.RootElement;

        if (TryGetArray(root, TicketsProperty, out JsonElement tickets)
            && tickets.GetArrayLength() > 0
            && TryGetString(tickets[0], TicketProperty) is string encodedTicket)
        {
            return Uri.UnescapeDataString(encodedTicket);
        }

        throw new EntitlementDeniedException(episodeId, CaseIdTextOf(root));
    }

    /// <summary>mako sends <c>caseId</c> as either a JSON string or a number; both read back as text.</summary>
    private static string? CaseIdTextOf(JsonElement root)
    {
        if (!root.TryGetProperty(CaseIdProperty, out JsonElement caseId))
        {
            return null;
        }

        return caseId.ValueKind switch
        {
            JsonValueKind.String => caseId.GetString(),
            JsonValueKind.Number => caseId.GetRawText(),
            _ => null,
        };
    }

    private static string AppendTicket(string mediaUrl, string ticket)
    {
        char separator = mediaUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{mediaUrl}{separator}{ticket}";
    }

    /// <summary>
    /// Best-effort only (spec §5.2): any failure describing the master playlist — a request error,
    /// a bot challenge, or text that parses to nothing usable — falls back to
    /// <see cref="StreamDescription.MakoDefault"/> rather than failing playback. Never logs
    /// <paramref name="finalUrl"/> itself, only its redacted host and path (N3).
    /// </summary>
    private async Task<StreamDescription> DescribeMasterPlaylistAsync(string finalUrl, CancellationToken cancellationToken)
    {
        try
        {
            string masterPlaylistText = await _http.GetTextAsync(finalUrl, cancellationToken).ConfigureAwait(false);
            return MasterPlaylist.Describe(masterPlaylistText);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "mako: could not describe the master playlist at {HostAndPath}; using the default stream description",
                MakoUrls.RedactedUrl(finalUrl));
            return StreamDescription.MakoDefault;
        }
    }

    // Every read of a mako JSON payload in this class goes through one of these guarded
    // accessors, matching MakoPages: a wrongly-typed or missing property degrades to null/false
    // instead of throwing (JsonElement.TryGetProperty throws on a non-object element).
    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryGetObject(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out value)
            && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetArray(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out value)
            && value.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        value = default;
        return false;
    }

    private readonly record struct PlaylistMedia(string Url, string Cdn, bool Protected);
}
