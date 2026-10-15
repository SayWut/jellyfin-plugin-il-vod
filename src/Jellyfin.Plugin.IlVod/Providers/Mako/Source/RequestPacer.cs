using Jellyfin.Plugin.IlVod.Cache;

namespace Jellyfin.Plugin.IlVod.Providers.Mako.Source;

/// <summary>
/// Keeps at most one request to <see cref="MakoUrls.BrowseHost"/> in flight and spaces consecutive
/// requests by <see cref="MinimumGap"/>, and tracks the back-off a bot challenge starts (N4). Time
/// comes from an injected <see cref="IClock"/> and the actual waiting from an injected delay
/// function, so a test can move both without a real clock or a real wait. Whether it is backing
/// off is decided again inside <see cref="EnterAsync"/>, after exclusive access is acquired — not
/// only by a caller's own <see cref="IsBackingOff"/> pre-check — so a caller already queued when a
/// concurrent request starts the back-off is refused too, instead of slipping through right after.
/// </summary>
public sealed class RequestPacer
{
    /// <summary>The shortest time allowed between the end of one request and the start of the next.</summary>
    public static readonly TimeSpan MinimumGap = TimeSpan.FromMilliseconds(750);

    /// <summary>How long a bot challenge keeps <see cref="IsBackingOff"/> true.</summary>
    public static readonly TimeSpan BackOffDuration = TimeSpan.FromMinutes(5);

    private readonly IClock _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    private DateTimeOffset? _lastExitAt;

    /// <summary>
    /// UTC ticks of the moment back-off ends, or 0 for "never backed off". A plain
    /// <c>DateTimeOffset?</c> field would not be safe to write and read from different threads
    /// without a lock — it is wider than one machine word, so a concurrent reader could observe a
    /// torn value. A single <c>long</c>, read and written through <see cref="Interlocked"/>, cannot
    /// tear.
    /// </summary>
    private long _backOffUntilUtcTicks;

    public RequestPacer(IClock clock, Func<TimeSpan, CancellationToken, Task> delay)
    {
        _clock = clock;
        _delay = delay;
    }

    public bool IsBackingOff => IsBackingOffAt(_clock.UtcNow);

    public void BackOff() =>
        Interlocked.Exchange(ref _backOffUntilUtcTicks, (_clock.UtcNow + BackOffDuration).UtcTicks);

    /// <summary>
    /// Waits for exclusive access and for the gap since the last request to have passed, then
    /// grants it — unless backing off is still true once access is granted, in which case it
    /// throws <see cref="BackOffRefusalException"/> instead. Dispose the result to let the next
    /// caller in and start its own gap.
    /// </summary>
    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _oneAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsBackingOff)
            {
                throw new BackOffRefusalException();
            }

            TimeSpan remainingGap = RemainingGap();
            if (remainingGap > TimeSpan.Zero)
            {
                await _delay(remainingGap, cancellationToken).ConfigureAwait(false);
            }

            return new Turn(this);
        }
        catch
        {
            _oneAtATime.Release();
            throw;
        }
    }

    private bool IsBackingOffAt(DateTimeOffset now)
    {
        long backOffUntilUtcTicks = Interlocked.Read(ref _backOffUntilUtcTicks);
        return backOffUntilUtcTicks != 0 && now.UtcTicks < backOffUntilUtcTicks;
    }

    private TimeSpan RemainingGap()
    {
        if (_lastExitAt is not { } lastExitAt)
        {
            return TimeSpan.Zero;
        }

        TimeSpan sinceLastExit = _clock.UtcNow - lastExitAt;
        TimeSpan remaining = MinimumGap - sinceLastExit;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private void Exit()
    {
        _lastExitAt = _clock.UtcNow;
        _oneAtATime.Release();
    }

    /// <summary>The handle a caller disposes to give up its exclusive turn.</summary>
    private sealed class Turn : IDisposable
    {
        private readonly RequestPacer _pacer;
        private bool _exited;

        public Turn(RequestPacer pacer)
        {
            _pacer = pacer;
        }

        public void Dispose()
        {
            if (_exited)
            {
                return;
            }

            _exited = true;
            _pacer.Exit();
        }
    }
}
