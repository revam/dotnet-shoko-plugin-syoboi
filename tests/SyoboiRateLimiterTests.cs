using System.Diagnostics;
using Shoko.Plugin.Syoboi.Http;
using Xunit;

namespace Shoko.Plugin.Syoboi.Tests;

/// <summary>
/// <see cref="SyoboiRateLimiter.ComputeDelay"/> is the whole decision the
/// rate limiter makes, pulled out as a pure function so these tests never
/// need to wait on a real clock. <see cref="WaitAsync_enforces_the_minimum_interval_between_real_calls"/>
/// is the one test that does wait, and only for a few tens of milliseconds.
/// </summary>
public class SyoboiRateLimiterTests
{
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

    [Fact]
    public void ComputeDelay_allows_the_first_request_immediately()
    {
        var delay = SyoboiRateLimiter.ComputeDelay(lastRequestAt: null, now: DateTimeOffset.UnixEpoch, OneSecond);

        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    public void ComputeDelay_allows_a_request_once_the_interval_has_fully_elapsed()
    {
        var last = DateTimeOffset.UnixEpoch;
        var now = last + OneSecond;

        var delay = SyoboiRateLimiter.ComputeDelay(last, now, OneSecond);

        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    public void ComputeDelay_allows_a_request_well_past_the_interval()
    {
        var last = DateTimeOffset.UnixEpoch;
        var now = last + TimeSpan.FromMinutes(1);

        var delay = SyoboiRateLimiter.ComputeDelay(last, now, OneSecond);

        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    public void ComputeDelay_waits_out_the_remainder_of_the_interval()
    {
        var last = DateTimeOffset.UnixEpoch;
        var now = last + TimeSpan.FromMilliseconds(400);

        var delay = SyoboiRateLimiter.ComputeDelay(last, now, OneSecond);

        Assert.Equal(TimeSpan.FromMilliseconds(600), delay);
    }

    [Fact]
    public void ComputeDelay_waits_the_full_interval_when_no_time_has_passed_at_all()
    {
        var last = DateTimeOffset.UnixEpoch;

        var delay = SyoboiRateLimiter.ComputeDelay(last, last, OneSecond);

        Assert.Equal(OneSecond, delay);
    }

    [Fact]
    public async Task WaitAsync_enforces_the_minimum_interval_between_real_calls()
    {
        var limiter = new SyoboiRateLimiter(minInterval: TimeSpan.FromMilliseconds(60));
        var stopwatch = Stopwatch.StartNew();
        var cancellationToken = TestContext.Current.CancellationToken;

        await limiter.WaitAsync(cancellationToken);
        var firstElapsed = stopwatch.Elapsed;
        await limiter.WaitAsync(cancellationToken);
        var secondElapsed = stopwatch.Elapsed;

        // The first call should never wait (nothing came before it); the
        // second must not run until at least the minimum interval after it.
        Assert.True(firstElapsed < TimeSpan.FromMilliseconds(50), $"First call waited {firstElapsed}.");
        Assert.True(secondElapsed - firstElapsed >= TimeSpan.FromMilliseconds(55), $"Second call only waited {secondElapsed - firstElapsed}.");
    }

    [Fact]
    public async Task WaitAsync_serializes_concurrent_callers_instead_of_letting_them_through_together()
    {
        var limiter = new SyoboiRateLimiter(minInterval: TimeSpan.FromMilliseconds(40));
        var stopwatch = Stopwatch.StartNew();
        var completedAt = new System.Collections.Concurrent.ConcurrentBag<TimeSpan>();

        var tasks = Enumerable.Range(0, 4).Select(async _ =>
        {
            await limiter.WaitAsync();
            completedAt.Add(stopwatch.Elapsed);
        });
        await Task.WhenAll(tasks);

        var ordered = completedAt.OrderBy(t => t).ToList();
        for (var i = 1; i < ordered.Count; i++)
            Assert.True(ordered[i] - ordered[i - 1] >= TimeSpan.FromMilliseconds(35), $"Calls {i - 1} and {i} were only {ordered[i] - ordered[i - 1]} apart.");
    }
}
