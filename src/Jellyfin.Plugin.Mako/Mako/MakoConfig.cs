using System.Text.Json;

namespace Jellyfin.Plugin.Mako.MakoApi;

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

    public static MakoConfig Parse(string mainConfigJson, string coreConfigJson, string notesConfigJson)
    {
        using JsonDocument main = JsonDocument.Parse(mainConfigJson);
        using JsonDocument core = JsonDocument.Parse(coreConfigJson);
        using JsonDocument notes = JsonDocument.Parse(notesConfigJson);

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
        using JsonDocument main = JsonDocument.Parse(mainConfigJson);
        return RequireString(main.RootElement, "sources.ksCoreConfig");
    }

    /// <summary>Reads the URL of the third config document (the core config's own field for it).</summary>
    public static string NotesUrlOf(string coreConfigJson)
    {
        using JsonDocument core = JsonDocument.Parse(coreConfigJson);
        return RequireString(core.RootElement, "sources.notes");
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
