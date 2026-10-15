using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.IlVod.Core;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IlVod.Aggregation;

/// <summary>
/// The channel's one catalogue over every enabled provider. Scopes each provider's ids
/// (<see cref="ProviderScopedId"/>), maps its category names onto the shared ones
/// (<see cref="CategoryMapper"/>) and routes every request back to the provider an id came from.
/// Opening the root asks every enabled provider at once, and one provider failing never fails the
/// others: it answers with its last good show list (<see cref="ShowListSnapshots"/>), or is left
/// out when it never had one. A disabled provider is never asked anything, so its content leaves
/// the channel on the next root refresh.
/// </summary>
public sealed class AggregateCatalog : ICatalog
{
    private readonly IReadOnlyList<IContentProvider> _providersInOrder;
    private readonly IReadOnlyDictionary<string, IContentProvider> _providers;
    private readonly Func<IReadOnlyCollection<string>> _readDisabledProviderIds;
    private readonly CategoryMapper _categories;
    private readonly ShowListSnapshots _snapshots;
    private readonly ILogger<AggregateCatalog> _logger;

    /// <exception cref="ArgumentException">Two providers share an id, or an id is not
    /// <see cref="ProviderScopedId.IsValidProviderId">valid</see>.</exception>
    /// <param name="readDisabledProviderIds">Read on every call, so toggling a provider on the
    /// settings page takes effect on the next request with no restart.</param>
    public AggregateCatalog(
        IEnumerable<IContentProvider> providers,
        Func<IReadOnlyCollection<string>> readDisabledProviderIds,
        CategoryMapper categories,
        ShowListSnapshots snapshots,
        ILogger<AggregateCatalog> logger)
    {
        _providersInOrder = providers.ToList();
        _providers = IndexById(_providersInOrder);
        _readDisabledProviderIds = readDisabledProviderIds;
        _categories = categories;
        _snapshots = snapshots;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Show>> GetShowsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyCollection<string> disabled = _readDisabledProviderIds();
        List<IContentProvider> enabled = _providersInOrder.Where(provider => !disabled.Contains(provider.Info.Id)).ToList();
        if (enabled.Count == 0)
        {
            return [];
        }

        ProviderShows[] answers = await Task.WhenAll(enabled.Select(provider => ShowsOfAsync(provider, cancellationToken))).ConfigureAwait(false);

        if (answers.All(answer => answer.Shows is null))
        {
            throw new NoProviderAnsweredException(answers.First(answer => answer.Failure is not null).Failure!);
        }

        return answers
            .Where(answer => answer.Shows is not null)
            .SelectMany(answer => answer.Shows!.Select(show => InChannel(answer.Provider, show)))
            .ToList();
    }

    public async Task<IReadOnlyList<Season>> GetSeasonsAsync(string showId, CancellationToken cancellationToken)
    {
        if (!TryRoute(showId, out IContentProvider? provider, out string? innerShowId))
        {
            return [];
        }

        return await provider.Catalog.GetSeasonsAsync(innerShowId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Episode>> GetEpisodesAsync(string showId, string seasonId, CancellationToken cancellationToken)
    {
        if (!TryRoute(showId, out IContentProvider? provider, out string? innerShowId))
        {
            return [];
        }

        IReadOnlyList<Episode> episodes = await provider.Catalog.GetEpisodesAsync(innerShowId, seasonId, cancellationToken).ConfigureAwait(false);
        return episodes.Select(episode => episode with { Id = Scope(provider, episode.Id) }).ToList();
    }

    /// <exception cref="StreamUnavailableException">The id names no provider this channel has.</exception>
    public Task<StreamLink> GetStreamLinkAsync(string episodeId, CancellationToken cancellationToken) =>
        TryRoute(episodeId, out IContentProvider? provider, out string? innerEpisodeId)
            ? provider.Catalog.GetStreamLinkAsync(innerEpisodeId, cancellationToken)
            : throw new StreamUnavailableException($"il-vod: episode id '{episodeId}' names no available provider");

    private static string Scope(IContentProvider provider, string innerId) => new ProviderScopedId(provider.Info.Id, innerId).ToString();

    private Show InChannel(IContentProvider provider, Show show) =>
        show with { Id = Scope(provider, show.Id), Genres = _categories.Map(provider.Info, show.Genres) };

    private bool TryRoute(
        string scopedId,
        [NotNullWhen(true)] out IContentProvider? provider,
        [NotNullWhen(true)] out string? innerId)
    {
        provider = null;
        innerId = null;
        if (!ProviderScopedId.TryParse(scopedId, out ProviderScopedId? id)
            || !_providers.TryGetValue(id.Value.ProviderId, out provider)
            || _readDisabledProviderIds().Contains(id.Value.ProviderId))
        {
            provider = null;
            _logger.LogInformation("il-vod: '{ScopedId}' names no available provider", scopedId);
            return false;
        }

        innerId = id.Value.InnerId;
        return true;
    }

    private async Task<ProviderShows> ShowsOfAsync(IContentProvider provider, CancellationToken cancellationToken)
    {
        string providerId = provider.Info.Id;
        try
        {
            IReadOnlyList<Show> shows = await provider.Catalog.GetShowsAsync(cancellationToken).ConfigureAwait(false);
            _snapshots.Save(providerId, shows);
            return new ProviderShows(provider, shows, Failure: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Deliberately broad: whatever one provider throws, the others' shows must still list.
            IReadOnlyList<Show>? snapshot = _snapshots.TryGet(providerId);
            _logger.LogWarning(
                "il-vod: {Provider} could not list its shows ({Reason}); {Fallback}",
                providerId,
                exception.Message,
                snapshot is null ? "leaving it out" : $"showing its last {snapshot.Count} known shows");
            return new ProviderShows(provider, snapshot, exception);
        }
    }

    private static Dictionary<string, IContentProvider> IndexById(IEnumerable<IContentProvider> providers)
    {
        Dictionary<string, IContentProvider> byId = new(StringComparer.Ordinal);
        foreach (IContentProvider provider in providers)
        {
            string id = provider.Info.Id;
            if (!ProviderScopedId.IsValidProviderId(id))
            {
                throw new ArgumentException($"'{id}' is not a valid provider id", nameof(providers));
            }

            if (!byId.TryAdd(id, provider))
            {
                throw new ArgumentException($"two providers share the id '{id}'", nameof(providers));
            }
        }

        return byId;
    }

    private sealed record ProviderShows(IContentProvider Provider, IReadOnlyList<Show>? Shows, Exception? Failure);
}
