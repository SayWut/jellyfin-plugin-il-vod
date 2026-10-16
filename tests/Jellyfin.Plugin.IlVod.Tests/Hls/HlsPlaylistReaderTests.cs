using Jellyfin.Plugin.IlVod.Hls;

namespace Jellyfin.Plugin.IlVod.Tests.Hls;

public class HlsPlaylistReaderTests
{
    private const string MasterUrl = "https://cdn.test/vod/master.m3u8?token=secret";
    private const string VariantUrl = "https://cdn.test/vod/high/index.m3u8";
    private const string Master = "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=500000\nlow/index.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=900000\nhigh/index.m3u8\n";
    private const string Variant = "#EXTM3U\n#EXTINF:600.0,\na.ts\n#EXTINF:42.5,\nb.ts\n#EXT-X-ENDLIST\n";

    private readonly RecordingLogger<HlsPlaylistReaderTests> _logger = new();

    [Fact]
    public async Task AReadableMasterIsItsText()
    {
        Assert.Equal(Master, await Reader(url => Master).ReadMasterAsync(MasterUrl, CancellationToken.None));
    }

    /// <summary>The stream's URL is right whether or not its playlist could be read just now, so a
    /// failure leaves it undescribed rather than failing playback — and logs no query (N3).</summary>
    [Fact]
    public async Task AnUnreadableMasterIsNoTextAndAWarningWithoutItsQuery()
    {
        string? text = await Reader(url => throw new HttpRequestException("refused")).ReadMasterAsync(MasterUrl, CancellationToken.None);

        Assert.Null(text);
        string warning = Assert.Single(_logger.Warnings);
        Assert.StartsWith("kan: could not describe the master playlist", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACancelledReadIsCancelledNotSwallowed()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Reader(url => throw new OperationCanceledException()).ReadMasterAsync(MasterUrl, CancellationToken.None));
    }

    [Fact]
    public async Task ALengthIsTheSumOfTheVariantsSegments()
    {
        TimeSpan? length = await Reader(url => url == VariantUrl ? Variant : throw new InvalidOperationException(url))
            .MeasureLengthAsync(VariantUrl, CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(642.5), length);
    }

    [Fact]
    public async Task NoVariantMeasuresNoLengthWithoutARequest()
    {
        Assert.Null(await Reader(url => throw new InvalidOperationException(url)).MeasureLengthAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task AnUnreadableVariantMeasuresNoLength()
    {
        Assert.Null(await Reader(url => throw new HttpRequestException("refused")).MeasureLengthAsync(VariantUrl, CancellationToken.None));
        Assert.StartsWith("kan: could not measure the length", Assert.Single(_logger.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void TheBestVariantIsResolvedAgainstTheMastersUrl()
    {
        Assert.Equal(VariantUrl, HlsPlaylistReader.BestVariantUrlOf(MasterUrl, Master));
    }

    [Fact]
    public void AMasterWithNoVariantHasNoBestVariantUrl()
    {
        Assert.Null(HlsPlaylistReader.BestVariantUrlOf(MasterUrl, "#EXTM3U\n"));
    }

    private HlsPlaylistReader Reader(Func<string, string> answer) =>
        new((url, _) => Task.FromResult(answer(url)), "kan", _logger);
}
