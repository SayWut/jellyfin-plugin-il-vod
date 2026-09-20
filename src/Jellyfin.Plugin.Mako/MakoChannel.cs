using Jellyfin.Plugin.Mako.Browsing;
using Jellyfin.Plugin.Mako.Catalog;
using Jellyfin.Plugin.Mako.Model;
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
    private const string ContentVersion = "1";

    private readonly ICatalog _catalog;
    private readonly ILogger<MakoChannel> _logger;

    public MakoChannel(ICatalog catalog, ILogger<MakoChannel> logger)
    {
        _catalog = catalog;
        _logger = logger;
    }

    public string Name => ChannelName;

    public string Description => "mako 12+ VOD, fetched on demand.";

    // Jellyfin discards its cached channel items when this value changes.
    public string DataVersion => ContentVersion;

    public string HomePageUrl => "https://www.mako.co.il/mako-vod";

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
        FolderId folder = FolderId.Parse(query.FolderId);
        _logger.LogInformation("mako: folder opened: {FolderKind} '{FolderValue}'", folder.Kind, folder.Value);

        List<ChannelItemInfo> items = await ItemsInside(folder, cancellationToken).ConfigureAwait(false);
        return new ChannelItemResult { Items = items, TotalRecordCount = items.Count };
    }

    public async Task<IEnumerable<MediaSourceInfo>> GetChannelItemMediaInfo(string id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("mako: play pressed: '{EpisodeId}'", id);

        StreamLink link = await _catalog.GetStreamLinkAsync(id, cancellationToken).ConfigureAwait(false);
        return [MediaSourceFactory.Create(id, link)];
    }

    private async Task<List<ChannelItemInfo>> ItemsInside(FolderId folder, CancellationToken cancellationToken)
    {
        switch (folder.Kind)
        {
            case FolderKind.Show:
                IReadOnlyList<Season> seasons = await _catalog.GetSeasonsAsync(folder.Value, cancellationToken).ConfigureAwait(false);
                return seasons.Select(season => ChannelItemMapper.ToFolder(season)).ToList();

            case FolderKind.Season:
                IReadOnlyList<Episode> episodes = await _catalog.GetEpisodesAsync(folder.Value, cancellationToken).ConfigureAwait(false);
                return episodes.Select(ChannelItemMapper.ToPlayable).ToList();

            default:
                IReadOnlyList<Show> shows = await _catalog.GetShowsAsync(cancellationToken).ConfigureAwait(false);
                return shows.Select(ChannelItemMapper.ToFolder).ToList();
        }
    }
}
