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
    /// hand Jellyfin's ffmpeg a URL. Only the requested episode's player is read: a page may carry
    /// another video's player (a promo), and a redirect may land on another episode.</summary>
    /// <exception cref="FormatException">The page has no player for <paramref name="entryId"/>, or
    /// its player names no HLS playlist on Redge's CDN.</exception>
    public static string ParseHlsUrl(string episodePageHtml, string entryId)
    {
        PlayerConfig config = PlayerConfigsOf(episodePageHtml).FirstOrDefault(config => config.Id == entryId)
            ?? throw new FormatException($"kan episode page has no player for entry {entryId}");

        return CheckedPlayerUrl(config.Hls ?? throw new FormatException("kan player configuration names no HLS file"));
    }

    /// <summary>Every player configuration on the page that reads as JSON, in page order.</summary>
    private static IEnumerable<PlayerConfig> PlayerConfigsOf(string html) =>
        PlayerConfigScript().Matches(html)
            .Select(script => TryRead(script.Groups["json"].Value))
            .OfType<PlayerConfig>();

    private static PlayerConfig? TryRead(string json)
    {
        try
        {
            using JsonDocument config = JsonDocument.Parse(json);
            JsonElement root = config.RootElement;
            return root.ValueKind == JsonValueKind.Object
                ? new PlayerConfig(Text(root, "id"), root.TryGetProperty("file", out JsonElement file) ? Text(file, "hls") : null)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string CheckedPlayerUrl(string hls)
    {
        string absolute = hls.StartsWith("//", StringComparison.Ordinal) ? Uri.UriSchemeHttps + ":" + hls : hls;
        bool isRedge = Uri.TryCreate(absolute, UriKind.Absolute, out Uri? url)
            && url.Scheme == Uri.UriSchemeHttps
            && url.Host.EndsWith(PlayerHostSuffix, StringComparison.OrdinalIgnoreCase);

        return isRedge ? absolute : throw new FormatException("kan player configuration names no playlist on Redge's CDN");
    }

    private sealed record PlayerConfig(string? Id, string? Hls);

    [GeneratedRegex("""<script[^>]*\bdata-redge-config\b[^>]*>(?<json>.*?)</script>""", RegexOptions.Singleline)]
    private static partial Regex PlayerConfigScript();
}
