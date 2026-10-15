using System.Text.Json.Nodes;

namespace Jellyfin.Plugin.IlVod.Providers.Reshet13.Source;

/// <summary>
/// One question for Kaltura: which <paramref name="Service"/>/<paramref name="Action"/> to call and
/// the body fields that ask it, in the order they are sent. <see cref="KalturaOttClient"/> adds the
/// fields every call carries (partner, client tag, session) in front of them.
/// </summary>
/// <param name="Subject">What is being asked about (<c>series 57</c>), for an exception message.</param>
public sealed record KalturaQuery(string Service, string Action, string Subject, JsonObject Fields)
{
    /// <summary>How the call is named in a message: <c>asset/list</c>.</summary>
    public string Name => $"{Service}/{Action}";
}
