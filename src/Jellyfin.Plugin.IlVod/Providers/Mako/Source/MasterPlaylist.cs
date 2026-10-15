using System.Globalization;

namespace Jellyfin.Plugin.IlVod.Providers.Mako.Source;

/// <summary>
/// Reads the highest-bandwidth variant of an HLS master playlist and turns its resolution,
/// bitrate and (when present) H.264 <c>CODECS</c> attribute into a <see cref="MakoStreamVariant"/>,
/// from which Jellyfin is told whether it can copy the video instead of re-encoding it (spec §5.2).
/// </summary>
public static class MasterPlaylist
{
    private const string StreamInfTag = "#EXT-X-STREAM-INF:";
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
    public static MakoStreamVariant? Describe(string masterPlaylistText) =>
        ParseVariants(masterPlaylistText).MaxBy(variant => variant.BitsPerSecond) is { } best
            ? Describe(best)
            : null;

    /// <summary>The URI of the highest-bandwidth variant exactly as the playlist spells it —
    /// usually relative to the master playlist's own URL; <c>null</c> when there is none.</summary>
    public static string? BestVariantUri(string masterPlaylistText) =>
        ParseVariants(masterPlaylistText)
            .Where(variant => variant.Uri is not null)
            .MaxBy(variant => variant.BitsPerSecond)
            ?.Uri;

    private static MakoStreamVariant Describe(StreamVariant variant)
    {
        (string profile, double? level) = ProfileAndLevelFrom(variant.Codecs);
        return new MakoStreamVariant(variant.Width, variant.Height, variant.BitsPerSecond, profile, level);
    }

    private static IEnumerable<StreamVariant> ParseVariants(string masterPlaylistText)
    {
        string[] lines = masterPlaylistText.Split('\n');
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string trimmedLine = lines[lineIndex].Trim();
            if (!trimmedLine.StartsWith(StreamInfTag, StringComparison.Ordinal))
            {
                continue;
            }

            IReadOnlyDictionary<string, string> attributes = ParseAttributes(trimmedLine[StreamInfTag.Length..]);
            if (TryReadBitsPerSecond(attributes) is not int bitsPerSecond)
            {
                continue;
            }

            (int? width, int? height) = ParseResolution(attributes.GetValueOrDefault(ResolutionAttribute));
            yield return new StreamVariant(
                bitsPerSecond, width, height, attributes.GetValueOrDefault(CodecsAttribute), UriFollowing(lines, lineIndex));
        }
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
            return (MakoStreams.UnmeasuredProfile, null);
        }

        string profileByte = hexBytes[..2];
        string levelByte = hexBytes[4..6];

        string profile = VideoProfileByAvcProfileByte.GetValueOrDefault(profileByte, MakoStreams.UnmeasuredProfile);
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

    private sealed record StreamVariant(int BitsPerSecond, int? Width, int? Height, string? Codecs, string? Uri);

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
