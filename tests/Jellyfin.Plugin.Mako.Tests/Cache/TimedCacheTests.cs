using Jellyfin.Plugin.Mako.Cache;
using Jellyfin.Plugin.Mako.Tests.Support;

namespace Jellyfin.Plugin.Mako.Tests.Cache;

public class TimedCacheTests : IDisposable
{
    private readonly string _persistencePath = Path.Combine(Path.GetTempPath(), $"mako-cache-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_persistencePath))
        {
            File.Delete(_persistencePath);
        }
    }

    [Fact]
    public void UnknownKeyHasNoValue()
    {
        TimedCache cache = new(new FakeClock());

        CacheRead read = cache.Read("show:shemesh");

        Assert.False(read.HasValue);
        Assert.False(read.IsFresh);
    }

    [Fact]
    public void AValueIsFreshInsideItsLifetime()
    {
        FakeClock clock = new();
        TimedCache cache = new(clock);
        cache.Write("show:shemesh", "page-body", TimeSpan.FromHours(12));

        clock.Advance(TimeSpan.FromHours(11));
        CacheRead read = cache.Read("show:shemesh");

        Assert.True(read.HasValue);
        Assert.True(read.IsFresh);
        Assert.Equal("page-body", read.Value);
    }

    [Fact]
    public void AValueTurnsStaleButStaysReadableAfterItsLifetime()
    {
        FakeClock clock = new();
        TimedCache cache = new(clock);
        cache.Write("show:shemesh", "page-body", TimeSpan.FromHours(12));

        clock.Advance(TimeSpan.FromHours(13));
        CacheRead read = cache.Read("show:shemesh");

        Assert.True(read.HasValue);
        Assert.False(read.IsFresh);
        Assert.Equal("page-body", read.Value);
    }

    [Fact]
    public void WritingAgainReplacesTheValueAndItsExpiry()
    {
        FakeClock clock = new();
        TimedCache cache = new(clock);
        cache.Write("show:shemesh", "first-body", TimeSpan.FromMinutes(5));

        clock.Advance(TimeSpan.FromMinutes(1));
        cache.Write("show:shemesh", "second-body", TimeSpan.FromHours(1));
        clock.Advance(TimeSpan.FromMinutes(30));
        CacheRead read = cache.Read("show:shemesh");

        Assert.Equal("second-body", read.Value);
        Assert.True(read.IsFresh);
    }

    [Fact]
    public void APersistedCacheSurvivesBeingRecreated()
    {
        FakeClock clock = new();
        TimedCache first = new(clock, _persistencePath);
        first.Write("show:shemesh", "page-body", TimeSpan.FromHours(12));

        TimedCache second = new(clock, _persistencePath);
        CacheRead read = second.Read("show:shemesh");

        Assert.True(read.HasValue);
        Assert.True(read.IsFresh);
        Assert.Equal("page-body", read.Value);
    }

    [Fact]
    public void ACorruptPersistenceFileMeansAnEmptyCache()
    {
        File.WriteAllText(_persistencePath, "{not json");

        TimedCache cache = new(new FakeClock(), _persistencePath);
        CacheRead read = cache.Read("show:shemesh");

        Assert.False(read.HasValue);
    }

    [Fact]
    public void MemoryOnlyCacheWritesNoFile()
    {
        TimedCache cache = new(new FakeClock());

        cache.Write("show:shemesh", "page-body", TimeSpan.FromHours(12));

        Assert.False(File.Exists(_persistencePath));
    }
}
