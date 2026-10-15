using Jellyfin.Plugin.IlVod.Providers.Mako.Source;
using Jellyfin.Plugin.IlVod.Tests.Support;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Mako;

public class RequestPacerTests
{
    private static readonly Func<TimeSpan, CancellationToken, Task> NoDelay = (_, _) => Task.CompletedTask;

    [Fact]
    public async Task TheFirstRequestIsNotDelayed()
    {
        List<TimeSpan> delays = [];
        RequestPacer pacer = new(new FakeClock(), Recording(delays));

        (await pacer.EnterAsync(CancellationToken.None)).Dispose();

        Assert.Empty(delays);
    }

    [Fact]
    public async Task ASecondRequestWaitsOutTheRemainderOfTheGap()
    {
        List<TimeSpan> delays = [];
        FakeClock clock = new();
        RequestPacer pacer = new(clock, Recording(delays));

        (await pacer.EnterAsync(CancellationToken.None)).Dispose();
        clock.Advance(TimeSpan.FromMilliseconds(200));
        (await pacer.EnterAsync(CancellationToken.None)).Dispose();

        Assert.Equal([TimeSpan.FromMilliseconds(550)], delays);
    }

    [Fact]
    public async Task NoDelayOnceTheGapHasPassed()
    {
        List<TimeSpan> delays = [];
        FakeClock clock = new();
        RequestPacer pacer = new(clock, Recording(delays));

        (await pacer.EnterAsync(CancellationToken.None)).Dispose();
        clock.Advance(RequestPacer.MinimumGap);
        (await pacer.EnterAsync(CancellationToken.None)).Dispose();

        Assert.Empty(delays);
    }

    [Fact]
    public async Task OnlyOneRequestIsInsideAtATime()
    {
        RequestPacer pacer = new(new FakeClock(), NoDelay);

        IDisposable first = await pacer.EnterAsync(CancellationToken.None);
        Task<IDisposable> secondEntry = pacer.EnterAsync(CancellationToken.None);

        Assert.False(secondEntry.IsCompleted);

        first.Dispose();
        IDisposable second = await secondEntry;

        Assert.NotNull(second);
    }

    [Fact]
    public async Task ACallerAlreadyWaitingForATurnIsRefusedIfBackOffStartsBeforeItsTurnBegins()
    {
        RequestPacer pacer = new(new FakeClock(), NoDelay);

        IDisposable holderOfTheTurn = await pacer.EnterAsync(CancellationToken.None);
        Task<IDisposable> waitingEntry = pacer.EnterAsync(CancellationToken.None);
        Assert.False(waitingEntry.IsCompleted);

        // The back-off starts while the second caller is already queued — its own IsBackingOff
        // pre-check (made before it started waiting) could not have seen this coming.
        pacer.BackOff();
        holderOfTheTurn.Dispose();

        await Assert.ThrowsAsync<BackOffRefusalException>(() => waitingEntry);
    }

    [Fact]
    public void BackingOffEndsAfterFiveMinutes()
    {
        FakeClock clock = new();
        RequestPacer pacer = new(clock, NoDelay);

        pacer.BackOff();
        Assert.True(pacer.IsBackingOff);

        clock.Advance(RequestPacer.BackOffDuration);
        Assert.False(pacer.IsBackingOff);
    }

    private static Func<TimeSpan, CancellationToken, Task> Recording(List<TimeSpan> delays) =>
        (delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        };
}
