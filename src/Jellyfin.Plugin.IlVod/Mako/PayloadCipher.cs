using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.IlVod.MakoApi;

/// <summary>
/// AES-192-CBC/PKCS7 encryption matching the mako Android app's own <c>Utils.decrypt</c>/
/// <c>Utils.encrypt</c>: a fixed initialization vector, and a key that is simply the UTF-8
/// bytes of a 24-character key text served by mako's own notes config.
/// </summary>
public static class PayloadCipher
{
    private static readonly byte[] InitializationVector = Encoding.ASCII.GetBytes("theExact16Chars=");
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string Decrypt(string keyText, string base64Ciphertext)
    {
        byte[] ciphertext = DecodeBase64(base64Ciphertext);
        byte[] plaintext = Transform(keyText, ciphertext, encrypt: false);
        return DecodeUtf8(plaintext);
    }

    public static string Encrypt(string keyText, string plaintext)
    {
        byte[] input = Encoding.UTF8.GetBytes(plaintext);
        byte[] ciphertext = Transform(keyText, input, encrypt: true);
        return Convert.ToBase64String(ciphertext);
    }

    public static string DecryptWithAnyKey(IReadOnlyList<string> keyTexts, string base64Ciphertext, string what)
    {
        foreach (string keyText in keyTexts)
        {
            try
            {
                return Decrypt(keyText, base64Ciphertext);
            }
            catch (PayloadDecryptException)
            {
                // Try the next key.
            }
        }

        throw new PayloadDecryptException($"could not decrypt {what} with any of {keyTexts.Count} keys");
    }

    private static byte[] Transform(string keyText, byte[] input, bool encrypt)
    {
        try
        {
            using Aes aes = Aes.Create();
            aes.Key = Encoding.UTF8.GetBytes(keyText);
            return encrypt
                ? aes.EncryptCbc(input, InitializationVector, PaddingMode.PKCS7)
                : aes.DecryptCbc(input, InitializationVector, PaddingMode.PKCS7);
        }
        catch (CryptographicException exception)
        {
            throw new PayloadDecryptException("payload could not be decrypted with the given key", exception);
        }
    }

    private static byte[] DecodeBase64(string base64Text)
    {
        string withoutWhitespace = RemoveWhitespace(base64Text);
        try
        {
            return Convert.FromBase64String(withoutWhitespace);
        }
        catch (FormatException exception)
        {
            throw new PayloadDecryptException("payload is not valid Base64", exception);
        }
    }

    private static string DecodeUtf8(byte[] bytes)
    {
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new PayloadDecryptException("decrypted payload is not valid UTF-8", exception);
        }
    }

    private static string RemoveWhitespace(string text)
    {
        return string.Concat(text.Where(character => !char.IsWhiteSpace(character)));
    }
}
