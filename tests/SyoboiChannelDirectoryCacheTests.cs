using Shoko.Plugin.Syoboi.Http;
using Shoko.Plugin.Syoboi.Models;
using Xunit;

namespace Shoko.Plugin.Syoboi.Tests;

public class SyoboiChannelDirectoryCacheTests
{
    private sealed class StubTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static SyoboiChannelDirectory MakeDirectory()
        => new(
            new Dictionary<int, SyoboiChannel> { [19] = new(19, "TOKYO MX", "ＭＸテレビ", 1) },
            new Dictionary<int, SyoboiChannelGroup> { [1] = new(1, "テレビ 関東") }
        );

    [Fact]
    public async Task Fetches_once_and_reuses_the_result()
    {
        var cache = new SyoboiChannelDirectoryCache();
        var fetches = 0;

        Task<SyoboiChannelDirectory?> Fetch(CancellationToken _)
        {
            fetches++;
            return Task.FromResult<SyoboiChannelDirectory?>(MakeDirectory());
        }

        var first = await cache.GetOrFetchAsync(Fetch, TestContext.Current.CancellationToken);
        var second = await cache.GetOrFetchAsync(Fetch, TestContext.Current.CancellationToken);

        Assert.Equal(1, fetches);
        Assert.Same(first, second);
        Assert.False(first.IsEmpty);
    }

    [Fact]
    public async Task Fetches_again_once_the_entry_has_aged_out()
    {
        var time = new StubTimeProvider(DateTimeOffset.UnixEpoch);
        var cache = new SyoboiChannelDirectoryCache(time, lifetime: TimeSpan.FromHours(1));
        var fetches = 0;

        Task<SyoboiChannelDirectory?> Fetch(CancellationToken _)
        {
            fetches++;
            return Task.FromResult<SyoboiChannelDirectory?>(MakeDirectory());
        }

        await cache.GetOrFetchAsync(Fetch, TestContext.Current.CancellationToken);
        time.Now = time.Now.AddHours(2);
        await cache.GetOrFetchAsync(Fetch, TestContext.Current.CancellationToken);

        Assert.Equal(2, fetches);
    }

    [Fact]
    public async Task A_failed_fetch_is_not_cached()
    {
        var cache = new SyoboiChannelDirectoryCache();
        var fetches = 0;

        Task<SyoboiChannelDirectory?> Fetch(CancellationToken _)
        {
            fetches++;
            return Task.FromResult<SyoboiChannelDirectory?>(null);
        }

        var directory = await cache.GetOrFetchAsync(Fetch, TestContext.Current.CancellationToken);
        await cache.GetOrFetchAsync(Fetch, TestContext.Current.CancellationToken);

        Assert.True(directory.IsEmpty);
        Assert.Equal(2, fetches);
    }

    [Fact]
    public async Task Clear_drops_the_cached_entry()
    {
        var cache = new SyoboiChannelDirectoryCache();
        var fetches = 0;

        Task<SyoboiChannelDirectory?> Fetch(CancellationToken _)
        {
            fetches++;
            return Task.FromResult<SyoboiChannelDirectory?>(MakeDirectory());
        }

        await cache.GetOrFetchAsync(Fetch, TestContext.Current.CancellationToken);
        cache.Clear();
        await cache.GetOrFetchAsync(Fetch, TestContext.Current.CancellationToken);

        Assert.Equal(2, fetches);
        Assert.True(cache.TryGet(out _));
    }
}
