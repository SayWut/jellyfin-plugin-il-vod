using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.IlVod.Providers.Kan;

/// <summary>
/// An episode's id: its program's id and its own entry id, <c>11510-1107310</c>. Both are needed to
/// play it — the episode page lives under the program's — and carrying both in the id means play
/// never has to look the program up. The transformer composes it; the source takes it apart.
/// </summary>
public static class KanEpisodeId
{
    private const char Separator = '-';

    public static string Of(string programId, string entryId) => $"{programId}{Separator}{entryId}";

    /// <returns><c>false</c> unless <paramref name="episodeId"/> is two runs of digits joined by the separator.</returns>
    public static bool TryParse(
        string episodeId,
        [NotNullWhen(true)] out string? programId,
        [NotNullWhen(true)] out string? entryId)
    {
        programId = null;
        entryId = null;
        string[] parts = episodeId.Split(Separator);
        if (parts is not [var program, var entry] || !IsNumericId(program) || !IsNumericId(entry))
        {
            return false;
        }

        (programId, entryId) = (program, entry);
        return true;
    }

    public static bool IsNumericId(string id) => id.Length > 0 && id.All(char.IsAsciiDigit);
}
