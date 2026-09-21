using Jellyfin.Plugin.Mako.Browsing;
using Jellyfin.Plugin.Mako.Catalog;
using Jellyfin.Plugin.Mako.MakoApi;
using Jellyfin.Plugin.Mako.Model;
using Jellyfin.Plugin.Mako.Transcoding;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Mako;

/// <summary>
/// The mako channel as Jellyfin sees it. Jellyfin calls in only when a user opens a folder
/// or presses play; this class never does work on its own.
/// </summary>
public sealed class MakoChannel : IChannel, IRequiresMediaInfoCallback
{
    private const string ChannelName = "mako";
    private const string ContentVersion = "4";
    private const string ChannelDescription = "mako 12+ VOD, fetched on demand.";
    private const string ChannelHomePageUrl = "https://www.mako.co.il/mako-vod";

    private const string ProbeSizeLimiterNotInstalledWarning =
        "mako: the ffmpeg probe-size limiter is NOT installed (Jellyfin's ITranscodeManager registration changed); playback will likely fail";

    private readonly ICatalog _catalog;
    private readonly ILogger<MakoChannel> _logger;

    public MakoChannel(ICatalog catalog, ILogger<MakoChannel> logger)
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
        FolderId? folder = TryParseFolderId(query.FolderId);
        if (folder is null)
        {
            return EmptyResult();
        }

        _logger.LogInformation("mako: folder opened: {FolderKind} '{FolderValue}'", folder.Kind, folder.Value);

        try
        {
            List<ChannelItemInfo> items = await ItemsInside(folder, cancellationToken).ConfigureAwait(false);
            return new ChannelItemResult { Items = items, TotalRecordCount = items.Count };
        }
        catch (Exception exception) when (exception is BotChallengeException or MakoRequestException)
        {
            // Jellyfin 12.1 does not catch this itself: an uncaught exception here would turn
            // opening a folder into an HTTP 500 in the Channels view instead of an empty one.
            // exception.Message is itself the reason (already scrubbed of any query string, per
            // N3, by BotChallengeException/MakoRequestException/MakoUrls.RedactedUrl).
            _logger.LogWarning(
                exception, "mako: could not open {FolderKind} '{FolderValue}': {Reason}; showing an empty view", folder.Kind, folder.Value, exception.Message);
            return EmptyResult();
        }
    }

    public async Task<IEnumerable<MediaSourceInfo>> GetChannelItemMediaInfo(string id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("mako: play pressed: '{EpisodeId}'", id);
        WarnIfProbeSizeLimiterIsNotInstalled();

        StreamLink link = await _catalog.GetStreamLinkAsync(id, cancellationToken).ConfigureAwait(false);
        return [MediaSourceFactory.Create(link)];
    }

    // Jellyfin's global -probesize buffers mako's unidentifiable data track in RAM until the
    // phone aborts playback (docs/spec.md §4.3); the plugin fixes this by wrapping
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

    private FolderId? TryParseFolderId(string? rawFolderId)
    {
        try
        {
            return FolderId.Parse(rawFolderId);
        }
        catch (FormatException)
        {
            _logger.LogWarning("mako: '{RawFolderId}' is not a mako folder id; showing an empty view.", rawFolderId);
            return null;
        }
    }

    private async Task<List<ChannelItemInfo>> ItemsInside(FolderId folder, CancellationToken cancellationToken)
    {
        switch (folder.Kind)
        {
            case FolderKind.Root:
                IReadOnlyList<Show> shows = await _catalog.GetShowsAsync(cancellationToken).ConfigureAwait(false);
                return shows.Select(ChannelItemMapper.ToFolder).ToList();

            case FolderKind.Show:
                IReadOnlyList<Season> seasons = await _catalog.GetSeasonsAsync(folder.Value, cancellationToken).ConfigureAwait(false);
                return seasons.Select(ChannelItemMapper.ToFolder).ToList();

            case FolderKind.Season:
                IReadOnlyList<Episode> episodes = await _catalog.GetEpisodesAsync(folder.Value, cancellationToken).ConfigureAwait(false);
                return episodes.Select(ChannelItemMapper.ToPlayable).ToList();

            default:
                throw new InvalidOperationException($"Folder kind '{folder.Kind}' has no items.");
        }
    }
}
