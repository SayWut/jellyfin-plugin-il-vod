using Jellyfin.Plugin.Mako.Cache;

namespace Jellyfin.Plugin.Mako.MakoApi;

/// <summary>
/// Keeps at most one request to <see cref="MakoUrls.BrowseHost"/> in flight and spaces consecutive
/// requests by <see cref="MinimumGap"/>, and tracks the back-off a bot challenge starts (N4). Time
/// comes from an injected <see cref="IClock"/> and the actual waiting from an injected delay
/// function, so a test can move both without a real clock or a real wait.
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
    private DateTimeOffset? _backOffUntil;

    public RequestPacer(IClock clock, Func<TimeSpan, CancellationToken, Task> delay)
    {
        _clock = clock;
        _delay = delay;
    }

    public bool IsBackingOff => _backOffUntil is { } until && _clock.UtcNow < until;

    public void BackOff() => _backOffUntil = _clock.UtcNow + BackOffDuration;

    /// <summary>
    /// Waits for exclusive access and for the gap since the last request to have passed, then
    /// grants it. Dispose the result to let the next caller in and start its own gap.
    /// </summary>
    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _oneAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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
