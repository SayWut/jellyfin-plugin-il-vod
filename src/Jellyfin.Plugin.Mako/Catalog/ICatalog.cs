using Jellyfin.Plugin.Mako.Model;

namespace Jellyfin.Plugin.Mako.Catalog;

/// <summary>
/// What a content source can answer. Each method is called only because a user opened a folder or pressed play.
/// </summary>
public interface ICatalog
{
    Task<IReadOnlyList<Show>> GetShowsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Season>> GetSeasonsAsync(string showId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Episode>> GetEpisodesAsync(string seasonId, CancellationToken cancellationToken);

    Task<StreamLink> GetStreamLinkAsync(string episodeId, CancellationToken cancellationToken);
}
