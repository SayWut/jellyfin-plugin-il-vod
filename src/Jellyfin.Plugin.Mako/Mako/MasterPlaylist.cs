using System.Globalization;
using Jellyfin.Plugin.Mako.Model;

namespace Jellyfin.Plugin.Mako.MakoApi;

/// <summary>
/// Reads the highest-bandwidth variant of an HLS master playlist and turns its resolution,
/// bitrate and (when present) H.264 <c>CODECS</c> attribute into a <see cref="StreamDescription"/>
/// Jellyfin can use to decide whether it can copy the video instead of re-encoding it (spec §5.2).
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

    public static StreamDescription Describe(string masterPlaylistText)
    {
        List<StreamVariant> variants = ParseVariants(masterPlaylistText).ToList();
        return variants.Count == 0
            ? StreamDescription.MakoDefault
            : Describe(variants.MaxBy(variant => variant.BitsPerSecond)!);
    }

    private static StreamDescription Describe(StreamVariant variant)
    {
        (string profile, double? level) = ProfileAndLevelFrom(variant.Codecs);
        return new StreamDescription(variant.Width, variant.Height, variant.BitsPerSecond, profile, level);
    }

    private static IEnumerable<StreamVariant> ParseVariants(string masterPlaylistText)
    {
        foreach (string line in masterPlaylistText.Split('\n'))
        {
            string trimmedLine = line.Trim();
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
            yield return new StreamVariant(bitsPerSecond, width, height, attributes.GetValueOrDefault(CodecsAttribute));
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
            return (StreamDescription.MakoDefault.VideoProfile, null);
        }

        string profileByte = hexBytes[..2];
        string levelByte = hexBytes[4..6];

        string profile = VideoProfileByAvcProfileByte.GetValueOrDefault(profileByte, StreamDescription.MakoDefault.VideoProfile);
        double? level = int.TryParse(levelByte, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int levelValue)
            ? levelValue / LevelHexDivisor
            : null;

        return (profile, level);
    }

    private sealed record StreamVariant(int BitsPerSecond, int? Width, int? Height, string? Codecs);

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
