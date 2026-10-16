using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.IlVod.Providers.Kan.Source;

/// <summary>
/// Reads an episode page's player configuration — the JSON in its <c>data-redge-config</c>
/// script — for the master playlist it plays: Redge's CDN, named scheme-relative.
/// </summary>
public static partial class KanPlayerConfig
{
    private const string PlayerHostSuffix = ".cdn-redge.media";

    /// <summary>Only Redge's CDN is ever played: a page naming any other host is not trusted to
    /// hand Jellyfin's ffmpeg a URL.</summary>
    /// <exception cref="FormatException">The page has no player, or its player names no HLS
    /// playlist on Redge's CDN.</exception>
    public static string ParseHlsUrl(string episodePageHtml)
    {
        Match config = PlayerConfigScript().Match(episodePageHtml);
        if (!config.Success)
        {
            throw new FormatException("kan episode page has no player configuration");
        }

        return CheckedPlayerUrl(HlsFileOf(config.Groups["json"].Value));
    }

    private static string HlsFileOf(string configJson)
    {
        try
        {
            using JsonDocument config = JsonDocument.Parse(configJson);
            return config.RootElement.TryGetProperty("file", out JsonElement file)
                && file.ValueKind == JsonValueKind.Object
                && file.TryGetProperty("hls", out JsonElement hls)
                && hls.ValueKind == JsonValueKind.String
                    ? hls.GetString()!
                    : throw new FormatException("kan player configuration names no HLS file");
        }
        catch (JsonException exception)
        {
            throw new FormatException("kan player configuration is not JSON", exception);
        }
    }

    private static string CheckedPlayerUrl(string hls)
    {
        string absolute = hls.StartsWith("//", StringComparison.Ordinal) ? Uri.UriSchemeHttps + ":" + hls : hls;
        bool isRedge = Uri.TryCreate(absolute, UriKind.Absolute, out Uri? url)
            && url.Scheme == Uri.UriSchemeHttps
            && url.Host.EndsWith(PlayerHostSuffix, StringComparison.OrdinalIgnoreCase);

        return isRedge ? absolute : throw new FormatException("kan player configuration names no playlist on Redge's CDN");
    }

    [GeneratedRegex("""<script[^>]*\bdata-redge-config\b[^>]*>(?<json>.*?)</script>""", RegexOptions.Singleline)]
    private static partial Regex PlayerConfigScript();
}
