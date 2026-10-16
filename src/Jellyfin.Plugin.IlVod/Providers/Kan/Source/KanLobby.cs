namespace Jellyfin.Plugin.IlVod.Providers.Kan.Source;

/// <summary>
/// The Kan BOX lobby: its genre rails, in order, and what its cards say about the programs they
/// show. Each rail shows only its first few programs, so the lobby names the genres and decorates
/// the shows but never lists them — the genre pages do.
/// </summary>
public sealed record KanLobby(IReadOnlyList<KanRail> Rails, IReadOnlyDictionary<string, KanLobbyCard> CardsByProgramId);
