using System.Globalization;

namespace Jellyfin.Plugin.IlVod.Providers.Kan.Source;

/// <summary>
/// An episode's length as Kan's feed spells it, which it does many ways: <c>00:29:35</c>, with a
/// trailing tab, space or dot, with milliseconds (<c>00:27:49.000</c>), as a frame timecode
/// (<c>00:49:27:00</c>), short (<c>29:35</c>, <c>1:00:23</c>) or as bare seconds (<c>42</c>).
/// </summary>
public static class KanDuration
{
    private const int TimecodeParts = 4;
    private const int ClockParts = 3;
    private const int SecondsPerMinute = 60;

    /// <returns><c>null</c> when <paramref name="text"/> states no length: nothing, nonsense, or
    /// zero, which is what the feed says when it does not know.</returns>
    public static TimeSpan? Parse(string? text)
    {
        string[] parts = (text ?? string.Empty).Trim().TrimEnd('.').Split(':');
        if (parts.Length == TimecodeParts)
        {
            parts = parts[..ClockParts];
        }

        parts[^1] = parts[^1].Split('.')[0];

        if (parts.Length > ClockParts || !TryReadSeconds(parts, out int seconds) || seconds == 0)
        {
            return null;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>Hours, minutes and seconds, as many as are given.</summary>
    private static bool TryReadSeconds(string[] parts, out int seconds)
    {
        seconds = 0;
        foreach (string part in parts)
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            {
                return false;
            }

            seconds = (seconds * SecondsPerMinute) + value;
        }

        return true;
    }
}
