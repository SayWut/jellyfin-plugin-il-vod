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

    private const string NoMediaMessage = "playlist has no media";
    private const string UnusableConfigMessage = "mako config is not valid JSON";

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
        MakoConfig config = await LoadConfigAsync(cancellationToken).ConfigureAwait(false);
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
            MakoConfig freshConfig = await LoadConfigAsync(cancellationToken).ConfigureAwait(false);
            return (freshConfig, PayloadCipher.DecryptWithAnyKey(freshConfig.PlaylistKeys, encryptedPlaylist, "playlist"));
        }
    }

    /// <summary>
    /// Loads mako's config chain, turning a config document that is not usable into one of this
    /// class's own typed failures. Play is a request Jellyfin answers directly, so a bare
    /// <see cref="FormatException"/> escaping here would be an HTTP 500 rather than a reported
    /// playback error.
    /// </summary>
    /// <exception cref="MakoRequestException">mako's config chain could not be parsed.</exception>
    private async Task<MakoConfig> LoadConfigAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _config.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (FormatException exception)
        {
            throw new MakoRequestException(UnusableConfigMessage, exception);
        }
    }

    /// <exception cref="MakoRequestException">
    /// The playlist is not valid JSON, or its <c>media</c> array is missing, not an array, empty,
    /// or contains no entry usable as a stream (a JSON object with a non-empty string <c>url</c>).
    /// </exception>
    private static PlaylistMedia ParseBestMedia(string playlistJson)
    {
        using JsonDocument document = ParseJsonOrThrow(playlistJson, "playlist");
        JsonElement root = document.RootElement;

        if (!TryGetArray(root, MediaProperty, out JsonElement mediaEntries)
            || HighestWeightUsableMedia(mediaEntries) is not { } bestMedia)
        {
            throw new MakoRequestException(NoMediaMessage);
        }

        return new PlaylistMedia(bestMedia.Url, bestMedia.Cdn, IsProtectedContent(root));
    }

    /// <summary>
    /// Entries that are not JSON objects, or have no non-empty string <c>url</c>, are ignored
    /// entirely when choosing — never surfaced as a playable stream with an empty URL. Ties among
    /// the remaining entries keep the first, since only a strictly higher weight ever replaces it.
    /// </summary>
    private static UsableMedia? HighestWeightUsableMedia(JsonElement mediaEntries)
    {
        UsableMedia? best = null;

        foreach (JsonElement candidate in mediaEntries.EnumerateArray())
        {
            if (TryReadUsableMedia(candidate) is not { } usable)
            {
                continue;
            }

            if (best is not { } current || usable.Weight > current.Weight)
            {
                best = usable;
            }
        }

        return best;
    }

    private static UsableMedia? TryReadUsableMedia(JsonElement media) =>
        TryGetString(media, MediaUrlProperty) is { Length: > 0 } url
            ? new UsableMedia(url, TryGetString(media, MediaCdnProperty) ?? string.Empty, CdnWeightOf(media))
            : null;

    /// <summary>
    /// <c>cdnLB</c> is the entry's weight, which mako sends as a JSON string or as a JSON number —
    /// mako's own app coerces either. Anything else, and an unparseable string, counts as 0.
    /// </summary>
    private static int CdnWeightOf(JsonElement media)
    {
        if (media.ValueKind != JsonValueKind.Object || !media.TryGetProperty(CdnWeightProperty, out JsonElement cdnWeight))
        {
            return 0;
        }

        return cdnWeight.ValueKind switch
        {
            JsonValueKind.String => WeightFromText(cdnWeight.GetString()),
            JsonValueKind.Number => cdnWeight.TryGetInt32(out int weight) ? weight : 0,
            _ => 0,
        };
    }

    private static int WeightFromText(string? weightText) =>
        int.TryParse(weightText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int weight) ? weight : 0;

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

    /// <exception cref="MakoRequestException">The token response is not valid JSON.</exception>
    /// <exception cref="EntitlementDeniedException">The token response carries no ticket (N5).</exception>
    private static string ExtractTicketOrThrow(string tokenJson, string episodeId)
    {
        using JsonDocument document = ParseJsonOrThrow(tokenJson, "token response");
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
            TimeSpan? duration = await MeasureDurationAsync(finalUrl, masterPlaylistText, cancellationToken).ConfigureAwait(false);
            return MasterPlaylist.Describe(masterPlaylistText) with { Duration = duration };
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

    /// <summary>
    /// mako states an episode's length nowhere, and Jellyfin 12.1 treats a source with no run
    /// time as a live stream — players then join 30-60 s in and cannot rewind. The CDN's variant
    /// playlist lists every segment, so its sum is the length: one more CDN request per play,
    /// none to mako's bot-protected host. Best-effort like the description itself: a failure
    /// leaves the duration unmeasured rather than failing playback.
    /// </summary>
    private async Task<TimeSpan?> MeasureDurationAsync(string masterPlaylistUrl, string masterPlaylistText, CancellationToken cancellationToken)
    {
        if (MasterPlaylist.BestVariantUri(masterPlaylistText) is not string variantUri
            || !Uri.TryCreate(new Uri(masterPlaylistUrl), variantUri, out Uri? variantUrl))
        {
            return null;
        }

        try
        {
            string variantPlaylistText = await _http.GetTextAsync(variantUrl.AbsoluteUri, cancellationToken).ConfigureAwait(false);
            return MediaPlaylist.TotalDuration(variantPlaylistText);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                "mako: could not measure the duration from {HostAndPath} ({Reason}); Jellyfin will treat the stream as live",
                MakoUrls.RedactedUrl(variantUrl.AbsoluteUri),
                exception.GetType().Name);
            return null;
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

    /// <summary>
    /// Parses mako's decrypted JSON, never letting a malformed payload surface as a raw
    /// <see cref="JsonException"/> — or quote the payload itself in the exception message (N3
    /// extends naturally to "never the plaintext either", not just the signed URL).
    /// </summary>
    /// <exception cref="MakoRequestException"><paramref name="json"/> is not valid JSON.</exception>
    private static JsonDocument ParseJsonOrThrow(string json, string what)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new MakoRequestException($"{what} is not valid JSON");
        }
    }

    private readonly record struct PlaylistMedia(string Url, string Cdn, bool Protected);

    private readonly record struct UsableMedia(string Url, string Cdn, int Weight);
}
