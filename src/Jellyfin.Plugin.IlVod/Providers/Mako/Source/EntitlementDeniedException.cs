namespace Jellyfin.Plugin.IlVod.Providers.Mako.Source;

/// <summary>
/// mako's entitlement service refused to issue a playback ticket for a protected episode: the
/// token response's <c>tickets</c> array was empty or missing (N5). Deliberately never worked
/// around by falling back to the unsigned media URL — unlike the Python prototype this plugin is
/// modelled on, which does exactly that. An unentitled item should fail loudly here, not play a
/// manifest mako itself never authorized.
/// </summary>
public sealed class EntitlementDeniedException : Exception
{
    public EntitlementDeniedException(string episodeId, string? caseId)
        : base(BuildMessage(episodeId, caseId))
    {
        CaseId = caseId;
    }

    public string? CaseId { get; }

    private static string BuildMessage(string episodeId, string? caseId) =>
        caseId is null
            ? $"mako refused entitlement for episode '{episodeId}'"
            : $"mako refused entitlement for episode '{episodeId}' (caseId={caseId})";
}
