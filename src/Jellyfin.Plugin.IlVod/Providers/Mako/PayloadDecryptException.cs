namespace Jellyfin.Plugin.IlVod.Providers.Mako;

/// <summary>
/// A mako payload — a playlist or a token response — could not be decrypted: the ciphertext
/// was not valid Base64, the key or padding was wrong, or the decrypted bytes were not UTF-8.
/// </summary>
public sealed class PayloadDecryptException : Exception
{
    public PayloadDecryptException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
