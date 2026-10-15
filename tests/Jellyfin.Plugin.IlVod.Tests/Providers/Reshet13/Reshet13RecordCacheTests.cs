using Jellyfin.Plugin.IlVod.Cache;
using Jellyfin.Plugin.IlVod.Providers.Reshet13;
using Jellyfin.Plugin.IlVod.Providers.Reshet13.Source;
using Jellyfin.Plugin.IlVod.Tests.Support;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Reshet13;

public class Reshet13RecordCacheTests
{
    private const string Key = "show:57";
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    private readonly TimedCache _timedCache = new(new FakeClock());

    [Fact]
    public void ARecordReadsBackAsWritten()
    {
        Reshet13RecordCache cache = new(_timedCache);
        Reshet13Episode episode = new("1", "פרק 1", null, null, null, null, null, [new KalturaImage("https://img/1", "16x9")], null);
        cache.Write(Key, new Reshet13ShowPage(new Dictionary<int, string> { [4] = "עונה 4" }, [episode]), Lifetime);

        Reshet13ShowPage? read = cache.ReadFresh<Reshet13ShowPage>(Key);

        Assert.NotNull(read);
        Assert.Equal("עונה 4", read.SeasonAssetNames[4]);
        Assert.Equal(episode.AssetId, Assert.Single(read.Episodes).AssetId);
        Assert.Equal("16x9", Assert.Single(read.Episodes[0].Images).Ratio);
    }

    [Theory]
    [InlineData("{\"SeasonAssetNames\":{}}")]
    [InlineData("{\"SeasonAssetNames\":{},\"Episodes\":null}")]
    [InlineData("{\"SeasonAssetNames\":{},\"Episodes\":[{\"AssetId\":\"1\",\"Name\":\"פרק 1\"}]}")]
    public void ACopyInAnotherShapeIsNoCopyAtAll(string otherShape)
    {
        _timedCache.Write(Key, otherShape, Lifetime);
        Reshet13RecordCache cache = new(_timedCache);

        Assert.Null(cache.ReadFresh<Reshet13ShowPage>(Key));
        Assert.Null(cache.ReadAny<Reshet13ShowPage>(Key));
    }
}
