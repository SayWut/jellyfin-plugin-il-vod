using Jellyfin.Plugin.IlVod.Core;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.IlVod.Api;

/// <summary>
/// What the settings page lists: every provider the plugin has, with its default category mapping,
/// and the shared category names. Read from the registered <see cref="ProviderInfo"/>s only, so
/// opening the settings page never builds a provider's network graph or reaches a provider.
/// </summary>
[ApiController]
[Route("IlVod")]
[Authorize(Policy = Policies.RequiresElevation)]
public sealed class ProvidersController : ControllerBase
{
    private readonly IEnumerable<ProviderInfo> _providers;

    public ProvidersController(IEnumerable<ProviderInfo> providers)
    {
        _providers = providers;
    }

    [HttpGet("Providers")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ProvidersResponse> GetProviders() =>
        new ProvidersResponse(
            _providers
                .Select(provider => new ProviderDto(provider.Id, provider.DisplayName, provider.HomePageUrl, provider.DefaultCategories))
                .ToList(),
            SharedCategories.All);

    public sealed record ProvidersResponse(IReadOnlyList<ProviderDto> Providers, IReadOnlyList<string> SharedCategories);

    public sealed record ProviderDto(string Id, string DisplayName, string HomePageUrl, IReadOnlyDictionary<string, string> DefaultCategories);
}
