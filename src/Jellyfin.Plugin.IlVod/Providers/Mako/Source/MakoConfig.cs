using System.Text.Json;

namespace Jellyfin.Plugin.IlVod.Providers.Mako.Source;

/// <summary>
/// The URL templates, app identity and decryption keys mako serves from its own three-stage
/// config chain (main config -> core config -> notes config), parsed once and reused for the
/// lifetime named in spec §2 (24 hours).
/// </summary>
public sealed record MakoConfig(
    string BaseUrlTemplate,
    string PlaylistUrlTemplate,
    string TokenUrl,
    string AppId,
    IReadOnlyList<string> PlaylistKeys,
    string TokenKey)
{
    private const string NewPlaylistKeyField = "notes.note1_n";
    private const string OldPlaylistKeyField = "notes.note1_o";

    // How each of the three documents of the chain names itself in an error about it.
    private const string MainDocument = "main";
    private const string CoreDocument = "core";
    private const string NotesDocument = "notes";

    public static MakoConfig Parse(string mainConfigJson, string coreConfigJson, string notesConfigJson)
    {
        using JsonDocument main = ParseOrThrow(mainConfigJson, MainDocument);
        using JsonDocument core = ParseOrThrow(coreConfigJson, CoreDocument);
        using JsonDocument notes = ParseOrThrow(notesConfigJson, NotesDocument);

        return new MakoConfig(
            BaseUrlTemplate: RequireString(main.RootElement, "sources.baseUrl"),
            PlaylistUrlTemplate: RequireString(core.RootElement, "sources.playlist"),
            TokenUrl: RequireString(core.RootElement, "sources.token"),
            AppId: RequireString(main.RootElement, "params.appId"),
            PlaylistKeys: RequirePlaylistKeys(notes.RootElement),
            TokenKey: RequireString(notes.RootElement, "notes.note3"));
    }

    /// <summary>Reads the URL of the second config document (the main config's own field for it).</summary>
    public static string CoreConfigUrlOf(string mainConfigJson)
    {
        using JsonDocument main = ParseOrThrow(mainConfigJson, MainDocument);
        return RequireString(main.RootElement, "sources.ksCoreConfig");
    }

    /// <summary>Reads the URL of the third config document (the core config's own field for it).</summary>
    public static string NotesUrlOf(string coreConfigJson)
    {
        using JsonDocument core = ParseOrThrow(coreConfigJson, CoreDocument);
        return RequireString(core.RootElement, "sources.notes");
    }

    /// <summary>
    /// Parses one document of the chain, naming which one failed and nothing else: the raw
    /// <c>JsonReaderException</c> quotes the text it choked on, and these documents carry mako's
    /// decryption keys — so neither the payload nor the parser's exception is passed on.
    /// </summary>
    /// <exception cref="FormatException"><paramref name="configJson"/> is not valid JSON.</exception>
    private static JsonDocument ParseOrThrow(string configJson, string whichDocument)
    {
        try
        {
            return JsonDocument.Parse(configJson);
        }
        catch (JsonException)
        {
            throw new FormatException($"mako {whichDocument} config is not valid JSON");
        }
    }

    private static IReadOnlyList<string> RequirePlaylistKeys(JsonElement notesRoot)
    {
        List<string> availableKeys = [];
        if (TryReadString(notesRoot, NewPlaylistKeyField) is string newKey)
        {
            availableKeys.Add(newKey);
        }

        if (TryReadString(notesRoot, OldPlaylistKeyField) is string oldKey)
        {
            availableKeys.Add(oldKey);
        }

        if (availableKeys.Count == 0)
        {
            throw new FormatException($"mako config is missing both '{NewPlaylistKeyField}' and '{OldPlaylistKeyField}'");
        }

        return availableKeys;
    }

    private static string RequireString(JsonElement root, string dottedFieldPath)
    {
        return TryReadString(root, dottedFieldPath)
            ?? throw new FormatException($"mako config is missing required field '{dottedFieldPath}'");
    }

    private static string? TryReadString(JsonElement root, string dottedFieldPath)
    {
        JsonElement current = root;
        foreach (string segment in dottedFieldPath.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }
}
