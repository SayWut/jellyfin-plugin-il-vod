using System.Globalization;

namespace Jellyfin.Plugin.IlVod.Hls;

/// <summary>
/// Reads the highest-bandwidth variant of an HLS master playlist and turns its resolution,
/// bitrate and (when present) H.264 <c>CODECS</c> attribute into a <see cref="HlsVariant"/>,
/// from which Jellyfin is told whether it can copy the video instead of re-encoding it (spec §5.2).
/// </summary>
public static class MasterPlaylist
{
    /// <summary>The H.264 profile assumed when a variant names no <c>CODECS</c>.</summary>
    public const string UnmeasuredProfile = "Main";

    private const string StreamInfTag = "#EXT-X-STREAM-INF:";
    private const string MediaTag = "#EXT-X-MEDIA:";
    private const string TypeAttribute = "TYPE";
    private const string AudioType = "AUDIO";
    private const string UriAttribute = "URI";
    private const string AudioGroupAttribute = "AUDIO";
    private const string GroupIdAttribute = "GROUP-ID";
    private const string BandwidthAttribute = "BANDWIDTH";
    private const string ResolutionAttribute = "RESOLUTION";
    private const string CodecsAttribute = "CODECS";
    private const string ResolutionSeparator = "x";
    private const string AvcCodecPrefix = "avc1.";

    // avc1.PPCCLL: PP is the profile byte, CC the constraint-flags byte (unused here), LL the
    // level byte. The level byte is ten times the decimal level (0x1F = 31 -> level 3.1).
    private const int AvcCodecHexLength = 6;
    private const double LevelHexDivisor = 10.0;

    private static readonly IReadOnlyDictionary<string, string> VideoProfileByAvcProfileByte =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["42"] = "Baseline",
            ["4D"] = "Main",
            ["58"] = "Extended",
            ["64"] = "High",
        };

    /// <summary>The highest-bandwidth variant; <c>null</c> when the playlist lists none.</summary>
    public static HlsVariant? Describe(string masterPlaylistText) =>
        RatedVariants(ListPlaylists(masterPlaylistText)).MaxBy(variant => variant.BitsPerSecond) is { } best
            ? Describe(best)
            : null;

    /// <summary>The URI of the highest-bandwidth variant exactly as the playlist spells it —
    /// usually relative to the master playlist's own URL; <c>null</c> when there is none.</summary>
    public static string? BestVariantUri(string masterPlaylistText) =>
        RatedVariants(ListPlaylists(masterPlaylistText))
            .Where(variant => variant.Uri is not null)
            .MaxBy(variant => variant.BitsPerSecond)
            ?.Uri;

    /// <summary>
    /// Where ffmpeg will find the best variant's video and the audio, for a master whose variants
    /// are video only and whose audio is one separate rendition (Kan's). ffmpeg's HLS demuxer opens
    /// one playlist per URI in the order the master lists them and numbers their streams in that
    /// order, one each here. <c>null</c> for any other layout — a variant with its own audio or no
    /// bandwidth, several renditions, a variant naming another audio group — whose numbering or
    /// best variant this cannot be sure of; the caller then claims nothing about the tracks.
    /// </summary>
    public static HlsStreamIndices? FfmpegStreamIndices(string masterPlaylistText)
    {
        List<ListedPlaylist> playlists = ListPlaylists(masterPlaylistText);
        List<ListedPlaylist> variants = [.. playlists.Where(playlist => playlist.IsVariant)];
        if (playlists.Except(variants).ToList() is not [{ Type: AudioType, Uri: not null } audio]
            || variants.Count == 0
            || !variants.All(variant => variant is { Uri: not null, BitsPerSecond: not null } && variant.AudioGroup == audio.GroupId)
            || playlists.Select(playlist => playlist.Uri).Distinct(StringComparer.Ordinal).Count() != playlists.Count)
        {
            return null;
        }

        ListedPlaylist bestVariant = variants.MaxBy(variant => variant.BitsPerSecond)!;
        return new HlsStreamIndices(Video: playlists.IndexOf(bestVariant), Audio: playlists.IndexOf(audio));
    }

    /// <summary>Every rendition and variant the master lists, in its order.</summary>
    private static List<ListedPlaylist> ListPlaylists(string masterPlaylistText)
    {
        string[] lines = masterPlaylistText.Split('\n');
        var playlists = new List<ListedPlaylist>();
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string trimmedLine = lines[lineIndex].Trim();
            if (trimmedLine.StartsWith(MediaTag, StringComparison.Ordinal))
            {
                playlists.Add(ToRendition(ParseAttributes(trimmedLine[MediaTag.Length..])));
            }
            else if (trimmedLine.StartsWith(StreamInfTag, StringComparison.Ordinal))
            {
                playlists.Add(ToVariant(ParseAttributes(trimmedLine[StreamInfTag.Length..]), UriFollowing(lines, lineIndex)));
            }
        }

        return playlists;
    }

    /// <summary>The variants that state their bandwidth, the only ones a best variant is chosen among.</summary>
    private static IEnumerable<ListedPlaylist> RatedVariants(IEnumerable<ListedPlaylist> playlists) =>
        playlists.Where(playlist => playlist is { IsVariant: true, BitsPerSecond: not null });

    private static ListedPlaylist ToRendition(IReadOnlyDictionary<string, string> attributes) => new(
        IsVariant: false,
        Type: attributes.GetValueOrDefault(TypeAttribute),
        GroupId: attributes.GetValueOrDefault(GroupIdAttribute),
        AudioGroup: null,
        BitsPerSecond: null,
        Resolution: (null, null),
        Codecs: null,
        Uri: attributes.GetValueOrDefault(UriAttribute));

    private static ListedPlaylist ToVariant(IReadOnlyDictionary<string, string> attributes, string? uri) => new(
        IsVariant: true,
        Type: null,
        GroupId: null,
        AudioGroup: attributes.GetValueOrDefault(AudioGroupAttribute),
        BitsPerSecond: TryReadBitsPerSecond(attributes),
        Resolution: ParseResolution(attributes.GetValueOrDefault(ResolutionAttribute)),
        Codecs: attributes.GetValueOrDefault(CodecsAttribute),
        Uri: uri);

    private static HlsVariant Describe(ListedPlaylist variant)
    {
        (string profile, double? level) = ProfileAndLevelFrom(variant.Codecs);
        return new HlsVariant(variant.Resolution.Width, variant.Resolution.Height, variant.BitsPerSecond, profile, level);
    }

    private static int? TryReadBitsPerSecond(IReadOnlyDictionary<string, string> attributes) =>
        attributes.TryGetValue(BandwidthAttribute, out string? bandwidth)
        && int.TryParse(bandwidth, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bitsPerSecond)
            ? bitsPerSecond
            : null;

    private static (int? Width, int? Height) ParseResolution(string? resolution)
    {
        string[]? parts = resolution?.Split(ResolutionSeparator);
        if (parts is not { Length: 2 }
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height))
        {
            return (null, null);
        }

        return (width, height);
    }

    private static (string Profile, double? Level) ProfileAndLevelFrom(string? codecs)
    {
        string? avcCodec = codecs
            ?.Split(',')
            .Select(codec => codec.Trim())
            .FirstOrDefault(codec => codec.StartsWith(AvcCodecPrefix, StringComparison.Ordinal));

        string hexBytes = avcCodec?[AvcCodecPrefix.Length..] ?? string.Empty;
        if (hexBytes.Length < AvcCodecHexLength)
        {
            return (UnmeasuredProfile, null);
        }

        string profileByte = hexBytes[..2];
        string levelByte = hexBytes[4..6];

        string profile = VideoProfileByAvcProfileByte.GetValueOrDefault(profileByte, UnmeasuredProfile);
        double? level = int.TryParse(levelByte, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int levelValue)
            ? levelValue / LevelHexDivisor
            : null;

        return (profile, level);
    }

    /// <summary>A variant's URI is the first line after its tag that is neither blank nor
    /// another tag; a tag arriving first means this variant has none.</summary>
    private static string? UriFollowing(string[] lines, int streamInfLineIndex)
    {
        string? next = lines
            .Skip(streamInfLineIndex + 1)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0);

        return next is null || next.StartsWith('#') ? null : next;
    }

    /// <summary>One playlist a master lists: a rendition (<c>#EXT-X-MEDIA</c>: its type and group) or
    /// a variant (its audio group, bandwidth, resolution and codecs).</summary>
    private sealed record ListedPlaylist(
        bool IsVariant,
        string? Type,
        string? GroupId,
        string? AudioGroup,
        int? BitsPerSecond,
        (int? Width, int? Height) Resolution,
        string? Codecs,
        string? Uri);

    private static IReadOnlyDictionary<string, string> ParseAttributes(string attributeList)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        int length = attributeList.Length;
        int position = 0;

        while (position < length)
        {
            int equalsIndex = attributeList.IndexOf('=', position);
            if (equalsIndex < 0)
            {
                break;
            }

            string name = attributeList[position..equalsIndex].Trim();
            (string value, int afterValue) = ReadAttributeValue(attributeList, equalsIndex + 1);
            attributes[name] = value;

            int nextAttributeSeparator = attributeList.IndexOf(',', afterValue);
            position = nextAttributeSeparator < 0 ? length : nextAttributeSeparator + 1;
        }

        return attributes;
    }

    /// <summary>
    /// Reads one attribute's value starting at <paramref name="valueStart"/>: a quoted value may
    /// itself contain commas, so its closing quote - not the next comma - ends it.
    /// </summary>
    private static (string Value, int NextPosition) ReadAttributeValue(string attributeList, int valueStart)
    {
        int length = attributeList.Length;
        if (valueStart < length && attributeList[valueStart] == '"')
        {
            int closingQuote = attributeList.IndexOf('"', valueStart + 1);
            int valueEnd = closingQuote < 0 ? length : closingQuote;
            return (attributeList[(valueStart + 1)..valueEnd], valueEnd < length ? valueEnd + 1 : length);
        }

        int commaIndex = attributeList.IndexOf(',', valueStart);
        int unquotedValueEnd = commaIndex < 0 ? length : commaIndex;
        return (attributeList[valueStart..unquotedValueEnd].Trim(), unquotedValueEnd);
    }
}
