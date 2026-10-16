namespace Jellyfin.Plugin.IlVod.Providers.Kan.Source;

/// <summary>
/// Joins the genre rails' walked cards into one <see cref="KanShowIndex"/>: each program once, where
/// a rail first listed it, with every genre it is on in rail order, decorated from the lobby and
/// kept only when it is Kan 11's.
/// </summary>
public static class KanShowIndexComposer
{
    /// <param name="cardsByRail">Every card of each of <paramref name="lobby"/>'s rails, in the rails' order.</param>
    /// <exception cref="FormatException">No program is Kan 11's: Kan always has some, so that is a
    /// failure, never an answer — Jellyfin would delete every show it holds.</exception>
    public static KanShowIndex Compose(KanLobby lobby, IReadOnlyList<IReadOnlyList<KanGenreCard>> cardsByRail)
    {
        List<KanShowCard> shows = [.. ListedPrograms(lobby.Rails, cardsByRail)
            .Where(program => KanUrls.IsInScope(program.Card.Href))
            .Select(program => ToShowCard(program, lobby))];

        return shows.Count > 0 ? new KanShowIndex(shows) : throw new FormatException("kan catalogue lists no Kan 11 show");
    }

    private static IEnumerable<ListedProgram> ListedPrograms(IReadOnlyList<KanRail> rails, IReadOnlyList<IReadOnlyList<KanGenreCard>> cardsByRail)
    {
        var programs = new Dictionary<string, ListedProgram>(StringComparer.Ordinal);
        var firstSeenOrder = new List<ListedProgram>();
        for (int railIndex = 0; railIndex < rails.Count; railIndex++)
        {
            foreach (KanGenreCard card in cardsByRail[railIndex])
            {
                if (!programs.TryGetValue(card.ProgramId, out ListedProgram? program))
                {
                    program = new ListedProgram(card, []);
                    programs[card.ProgramId] = program;
                    firstSeenOrder.Add(program);
                }

                program.AddGenre(rails[railIndex].Name);
            }
        }

        return firstSeenOrder;
    }

    private static KanShowCard ToShowCard(ListedProgram program, KanLobby lobby)
    {
        KanLobbyCard? lobbyCard = lobby.CardsByProgramId.GetValueOrDefault(program.Card.ProgramId);
        return new KanShowCard(
            program.Card.ProgramId,
            program.Card.Name,
            program.Card.ImageUrl,
            lobbyCard?.PosterUrl,
            lobbyCard?.Overview,
            program.Genres);
    }

    private sealed record ListedProgram(KanGenreCard Card, List<string> Genres)
    {
        public void AddGenre(string genre)
        {
            if (!Genres.Contains(genre))
            {
                Genres.Add(genre);
            }
        }
    }
}
