using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.IlVod.Aggregation;

/// <summary>
/// An id as the channel hands it to Jellyfin: the provider's own id for a show or episode,
/// prefixed with the provider's <see cref="Core.ProviderInfo.Id"/> so ids from different
/// providers can never collide and every request routes back to the provider it came from.
/// Split on the first separator only, so a provider's own id may contain it freely.
/// </summary>
public readonly record struct ProviderScopedId(string ProviderId, string InnerId)
{
    private const char Separator = ':';

    public override string ToString() => $"{ProviderId}{Separator}{InnerId}";

    /// <summary>A provider id is lower-case ASCII letters and digits only, and never empty.</summary>
    public static bool IsValidProviderId(string? providerId) =>
        !string.IsNullOrEmpty(providerId) && providerId.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character));

    public static bool TryParse(string? raw, [NotNullWhen(true)] out ProviderScopedId? id)
    {
        id = null;
        int separatorAt = raw?.IndexOf(Separator, StringComparison.Ordinal) ?? -1;
        if (separatorAt <= 0 || separatorAt == raw!.Length - 1)
        {
            return false;
        }

        string providerId = raw[..separatorAt];
        if (!IsValidProviderId(providerId))
        {
            return false;
        }

        id = new ProviderScopedId(providerId, raw[(separatorAt + 1)..]);
        return true;
    }
}
