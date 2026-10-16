namespace Jellyfin.Plugin.IlVod.Providers.Kan.Source;

/// <summary>What only the lobby says about a program: its 2:3 poster, absolute and without a size,
/// and its one-line synopsis; each <c>null</c> when the card has none.</summary>
public sealed record KanLobbyCard(string? PosterUrl, string? Overview);
