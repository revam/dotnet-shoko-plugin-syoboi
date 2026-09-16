using System;
using System.Threading;
using System.Threading.Tasks;

namespace Shoko.Plugin.Syoboi.Http;

/// <summary>
/// Enforces cal.syoboi.jp's request rate limit (one request per second for a
/// client with a custom User-Agent) across every call this plugin makes,
/// whatever request kicked it off.
/// </summary>
/// <remarks>
/// The decision of how long to wait is a pure function of "when was the last
/// request" and "what's the minimum interval", so it is split out as
/// <see cref="ComputeDelay"/> and tested directly without needing to await
/// a real (or simulated) delay. Only <see cref="WaitAsync"/> itself touches
/// the clock and <see cref="Task.Delay(TimeSpan,TimeProvider,CancellationToken)"/>.
/// </remarks>
public sealed class SyoboiRateLimiter
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _minInterval;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset? _lastRequestAt;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyoboiRateLimiter"/> class.
    /// </summary>
    /// <param name="timeProvider">
    /// Optional. The time provider to use. Defaults to <see cref="TimeProvider.System"/>.
    /// </param>
    /// <param name="minInterval">
    /// Optional. The minimum interval to enforce between requests. Defaults to
    /// <see cref="SyoboiConstants.MinRequestInterval"/>.
    /// </param>
    public SyoboiRateLimiter(TimeProvider? timeProvider = null, TimeSpan? minInterval = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _minInterval = minInterval ?? SyoboiConstants.MinRequestInterval;
    }

    /// <summary>
    /// Waits until it is safe to make another request, then reserves the slot.
    /// Concurrent callers are serialized and each pays whatever delay is left
    /// after the caller(s) ahead of it.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token that cancels the wait.
    /// </param>
    public async Task WaitAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var delay = ComputeDelay(_lastRequestAt, _timeProvider.GetUtcNow(), _minInterval);
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);

            _lastRequestAt = _timeProvider.GetUtcNow();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Computes how long to wait before the next request is allowed.
    /// </summary>
    /// <param name="lastRequestAt">
    /// When the previous request was allowed to proceed, or <c>null</c> if
    /// there hasn't been one yet.
    /// </param>
    /// <param name="now">
    /// The current time.
    /// </param>
    /// <param name="minInterval">
    /// The minimum interval to enforce between requests.
    /// </param>
    /// <returns>
    /// <see cref="TimeSpan.Zero"/> if a request may proceed immediately,
    /// otherwise how much longer to wait.
    /// </returns>
    internal static TimeSpan ComputeDelay(DateTimeOffset? lastRequestAt, DateTimeOffset now, TimeSpan minInterval)
    {
        if (lastRequestAt is not { } last)
            return TimeSpan.Zero;

        var elapsed = now - last;
        return elapsed >= minInterval ? TimeSpan.Zero : minInterval - elapsed;
    }
}
