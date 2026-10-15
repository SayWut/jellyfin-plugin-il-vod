namespace Jellyfin.Plugin.IlVod.Providers.Mako.Source;

/// <summary>
/// A <see cref="RequestPacer"/> refused to grant a turn because it was already backing off — a
/// decision made only AFTER exclusive access was acquired, so a caller that started waiting
/// before the back-off began (and so could not see it in a plain <see cref="RequestPacer.IsBackingOff"/>
/// pre-check) is refused too, instead of being let through onto the network (N4). <see
/// cref="RequestPacer"/> has no URL to name, so it raises this instead of a
/// <see cref="BotChallengeException"/>; a caller that knows which URL it was about to fetch
/// (<see cref="MakoHttpClient"/>) catches this and translates it into one.
/// </summary>
public sealed class BackOffRefusalException : Exception
{
    public BackOffRefusalException()
        : base("mako: refused a turn because the request pacer is backing off")
    {
    }
}
