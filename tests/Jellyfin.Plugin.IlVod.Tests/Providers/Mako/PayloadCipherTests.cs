using Jellyfin.Plugin.IlVod.Providers.Mako.Source;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Mako;

public class PayloadCipherTests
{
    private const string PlaylistKey = "TESTKEY-PLAYLIST-NEW-24b";
    private const string SecondPlaylistKey = "TESTKEY-PLAYLIST-OLD-24b";

    [Fact]
    public void WhatIsEncryptedDecryptsBackToTheSameText()
    {
        const string plaintext = "{\"title\":\"שמש\",\"season\":1}";

        string ciphertext = PayloadCipher.Encrypt(PlaylistKey, plaintext);
        string roundTripped = PayloadCipher.Decrypt(PlaylistKey, ciphertext);

        Assert.Equal(plaintext, roundTripped);
    }

    [Fact]
    public void DecryptsBase64ThatIsWrappedAcrossLines()
    {
        string plaintext = "{\"episodes\":[" + string.Concat(Enumerable.Repeat("\"episode-id-value\",", 15)) + "\"end\"]}";
        string ciphertext = PayloadCipher.Encrypt(PlaylistKey, plaintext);
        string wrapped = WrapEvery76Characters(ciphertext);

        string decrypted = PayloadCipher.Decrypt(PlaylistKey, wrapped);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void AWrongKeyIsReportedAsADecryptFailure()
    {
        string ciphertext = PayloadCipher.Encrypt(PlaylistKey, "{\"ok\":true}");

        Assert.Throws<PayloadDecryptException>(() => PayloadCipher.Decrypt(SecondPlaylistKey, ciphertext));
    }

    [Fact]
    public void GarbageInputIsReportedAsADecryptFailure()
    {
        Assert.Throws<PayloadDecryptException>(() => PayloadCipher.Decrypt(PlaylistKey, "not base64!!"));
    }

    [Fact]
    public void FallsBackToTheSecondKeyWhenTheFirstFails()
    {
        string ciphertext = PayloadCipher.Encrypt(SecondPlaylistKey, "{\"ok\":true}");
        string[] keys = [PlaylistKey, SecondPlaylistKey];

        string decrypted = PayloadCipher.DecryptWithAnyKey(keys, ciphertext, "playlist");

        Assert.Equal("{\"ok\":true}", decrypted);
    }

    [Fact]
    public void ReportsWhatCouldNotBeDecryptedWhenEveryKeyFails()
    {
        string ciphertext = "not base64!!";
        string[] keys = [PlaylistKey, SecondPlaylistKey];

        PayloadDecryptException exception = Assert.Throws<PayloadDecryptException>(
            () => PayloadCipher.DecryptWithAnyKey(keys, ciphertext, "playlist"));

        Assert.Contains("playlist", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchesAKnownVector()
    {
        // Generated on this machine with the Python prototype's own cipher, so the C# port is
        // pinned against the app's algorithm, not only against itself:
        //   python3 -c "from Cryptodome.Cipher import AES; from Cryptodome.Util.Padding import pad; \
        //     import base64; key = b'TESTKEY-PLAYLIST-NEW-24b'; iv = b'theExact16Chars='; \
        //     c = AES.new(key, AES.MODE_CBC, iv); \
        //     print(base64.b64encode(c.encrypt(pad(b'{\"ok\":true}', 16))).decode())"
        const string knownCiphertext = "d3ze0zQ7QwL2NxdvYPcjpw==";

        string decrypted = PayloadCipher.Decrypt(PlaylistKey, knownCiphertext);

        Assert.Equal("{\"ok\":true}", decrypted);
    }

    private static string WrapEvery76Characters(string text)
    {
        const int lineLength = 76;
        IEnumerable<string> lines = Enumerable.Range(0, (text.Length + lineLength - 1) / lineLength)
            .Select(lineIndex => text.Substring(lineIndex * lineLength, Math.Min(lineLength, text.Length - (lineIndex * lineLength))));
        return string.Join('\n', lines);
    }
}
