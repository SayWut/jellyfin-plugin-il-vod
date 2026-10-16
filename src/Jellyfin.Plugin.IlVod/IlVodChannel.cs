using Jellyfin.Plugin.IlVod.Aggregation;
using Jellyfin.Plugin.IlVod.Browsing;
using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Transcoding;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IlVod;

/// <summary>
/// The IL-VOD channel as Jellyfin sees it: one channel, one tile, every enabled provider's shows.
/// Jellyfin calls in only when a user opens a folder or presses play; this class never does work
/// on its own — not even in its constructor.
/// </summary>
/// <remarks>
/// Jellyfin 12.1 constructs every <see cref="IChannel"/> while the server is still starting and
/// long before any plugin instance exists: <c>ApplicationHost.InitializeServices</c> calls
/// <c>SetStaticProperties</c>, which resolves <c>IChannelManager</c>, whose constructor
/// materialises <c>IEnumerable&lt;IChannel&gt;</c> — all of it before <c>FindParts</c> calls
/// <c>CreatePlugins</c> and <c>Plugin.Instance</c> is set. An exception from here does not
/// disable the plugin; it stops the server from booting. That is why the catalogue arrives as a
/// <see cref="Lazy{T}"/>: building it reads the plugin's data folder and configuration, so it may
/// only happen on the first user action, never at construction.
/// </remarks>
public sealed class IlVodChannel : IChannel, IRequiresMediaInfoCallback
{
    private const string ChannelName = "IL-VOD";
    private const string ContentVersion = "9";
    private const string ChannelDescription = "Israeli VOD providers, fetched on demand.";
    private const string ChannelHomePageUrl = "";

    private const string ProbeSizeLimiterNotInstalledWarning =
        "il-vod: the ffmpeg probe-size limiter is NOT installed (Jellyfin's ITranscodeManager registration changed); playback will likely fail";

    private readonly Lazy<ICatalog> _catalog;
    private readonly ILogger<IlVodChannel> _logger;

    public IlVodChannel(Lazy<ICatalog> catalog, ILogger<IlVodChannel> logger)
    {
        _catalog = catalog;
        _logger = logger;
    }

    public string Name => ChannelName;

    public string Description => ChannelDescription;

    // Jellyfin discards its cached channel items when this value changes.
    public string DataVersion => ContentVersion;

    public string HomePageUrl => ChannelHomePageUrl;

    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

    public InternalChannelFeatures GetChannelFeatures() => new()
    {
        MediaTypes = [ChannelMediaType.Video],
        ContentTypes = [ChannelMediaContentType.Episode],
    };

    public bool IsEnabledFor(string userId) => true;

    public IEnumerable<ImageType> GetSupportedChannelImages() => [];

    public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        => Task.FromResult(new DynamicImageResponse { HasImage = false });

    public async Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        RefuseUnlessOpenedByAUser(query);

        FolderId? folder = TryParseFolderId(query.FolderId);
        if (folder is null)
        {
            return EmptyResult();
        }

        _logger.LogInformation("il-vod: folder opened: {FolderKind} '{FolderValue}'", folder.Kind, folder.Value);

        try
        {
            List<ChannelItemInfo> items = await ItemsInside(folder, cancellationToken).ConfigureAwait(false);
            return new ChannelItemResult { Items = items, TotalRecordCount = items.Count };
        }
        catch (Exception exception) when (exception is ProviderUnavailableException or FormatException)
        {
            // The provider failed (FormatException is a provider answering something that is not
            // its page — e.g. an HTML error page with status 200). Rethrown, never answered with
            // an empty list:
            // Jellyfin 12.1 takes any answer as the folder's full contents, caches it for 3 hours
            // and deletes every item it holds that the answer lacks. A failure is neither cached
            // nor acted on, so the next open asks again. exception.Message is already scrubbed of
            // any query string (N3) and of any payload text.
            _logger.LogWarning(
                "il-vod: could not open {FolderKind} '{FolderValue}': {Reason}", folder.Kind, folder.Value, exception.Message);
            throw;
        }
        catch (ArgumentException exception)
        {
            // A folder id the provider refuses as an address (e.g. a page path MakoUrls rejects).
            // That is wrong for good, not for now, so an empty view is the honest answer and
            // caching it costs nothing.
            _logger.LogWarning(
                "il-vod: could not open {FolderKind} '{FolderValue}': {Reason}; showing an empty view", folder.Kind, folder.Value, exception.Message);
            return EmptyResult();
        }
    }

    public async Task<IEnumerable<MediaSourceInfo>> GetChannelItemMediaInfo(string id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("il-vod: play pressed: '{EpisodeId}'", id);
        WarnIfProbeSizeLimiterIsNotInstalled();

        StreamLink link = await _catalog.Value.GetStreamLinkAsync(id, cancellationToken).ConfigureAwait(false);
        return [MediaSourceFactory.Create(link, ProviderIdOf(id))];
    }

    // Jellyfin 12.1 walks folders by itself: every new Season item is queued for a metadata
    // refresh, and SeasonMetadataService asks for the season's episodes (Season.GetEpisodes →
    // Series.GetSeasonEpisodes → ChannelManager.GetChannelItemsInternal) — so one user opening
    // a show made Jellyfin open all of its seasons within seconds, which mako answered with a
    // bot challenge. Those queries carry no user; a person's click always does.
    private void RefuseUnlessOpenedByAUser(InternalChannelItemQuery query)
    {
        if (query.UserId == Guid.Empty)
        {
            _logger.LogDebug("il-vod: folder '{RawFolderId}' asked for by Jellyfin itself; refused", query.FolderId);
            throw new NotOpenedByAUserException(query.FolderId ?? string.Empty);
        }
    }

    // Jellyfin's global -probesize buffers some providers' unidentifiable data tracks (mako's) in
    // RAM until the phone aborts playback (docs/spec.md §4.3); the plugin fixes this by wrapping
    // ITranscodeManager at startup, but that wrapping can silently fail to install if a future
    // Jellyfin update changes the seam it relies on. Checked here, on every play, so that
    // failure is loud instead of surfacing only as mysterious playback crashes.
    private void WarnIfProbeSizeLimiterIsNotInstalled()
    {
        if (!ProbeSizeLimiterStatus.IsInstalled)
        {
            _logger.LogWarning(ProbeSizeLimiterNotInstalledWarning);
        }
    }

    private static ChannelItemResult EmptyResult() => new() { Items = [], TotalRecordCount = 0 };

    // The catalogue has already routed the id to its provider by now, so it is scoped; an id that
    // is not (only ever a test's) names no provider, and no provider's play-time handling applies.
    private static string ProviderIdOf(string episodeId) =>
        ProviderScopedId.TryParse(episodeId, out ProviderScopedId? scoped) ? scoped.Value.ProviderId : string.Empty;

    private FolderId? TryParseFolderId(string? rawFolderId)
    {
        try
        {
            return FolderId.Parse(rawFolderId);
        }
        catch (FormatException)
        {
            _logger.LogWarning("il-vod: '{RawFolderId}' is not an IL-VOD folder id; showing an empty view.", rawFolderId);
            return null;
        }
    }

    private async Task<List<ChannelItemInfo>> ItemsInside(FolderId folder, CancellationToken cancellationToken)
    {
        ICatalog catalog = _catalog.Value;
        switch (folder.Kind)
        {
            case FolderKind.Root:
                IReadOnlyList<Show> shows = await catalog.GetShowsAsync(cancellationToken).ConfigureAwait(false);
                return shows.Select(ChannelItemMapper.ToFolder).ToList();

            case FolderKind.Show:
                IReadOnlyList<Season> seasons = await catalog.GetSeasonsAsync(folder.Value, cancellationToken).ConfigureAwait(false);
                return seasons.Select(season => ChannelItemMapper.ToFolder(season, folder.Value)).ToList();

            case FolderKind.Season:
                (string showId, string seasonId) = folder.SeasonParts();
                IReadOnlyList<Episode> episodes = await catalog.GetEpisodesAsync(showId, seasonId, cancellationToken).ConfigureAwait(false);
                return episodes.Select(ChannelItemMapper.ToPlayable).ToList();

            default:
                throw new InvalidOperationException($"Folder kind '{folder.Kind}' has no items.");
        }
    }
}
