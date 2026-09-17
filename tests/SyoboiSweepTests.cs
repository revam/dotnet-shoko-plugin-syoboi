using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shoko.Abstractions.Metadata.Airing;
using Shoko.Abstractions.Metadata.Anidb;
using Shoko.Plugin.Syoboi.Http;
using Xunit;

namespace Shoko.Plugin.Syoboi.Tests;

/// <summary>
/// Tests for the core-driven sweep: the server hands the provider a cursor
/// and a deadline, and expects a chunk that stops when the deadline fires and
/// says where the next one picks up.
/// </summary>
public class SyoboiSweepTests
{
    // One more than a single ProgLookup request covers, so a sweep of this
    // many anime is two requests and can be cut in half by a deadline.
    private const int TwoBatches = SyoboiApiClient.MaxTitleIdsPerRequest + 50;

    #region Cursors

    [Fact]
    public async Task A_sweep_that_walks_every_tracked_anime_ends_with_no_cursor()
    {
        using var host = new SweepHost(Anime(count: 3));

        var cursor = await host.Provider.SweepAsync(null, TestContext.Current.CancellationToken);

        Assert.Null(cursor);
        // The channel directory, then the one request the three titles share.
        Assert.Equal(3, host.Requests.Count);
    }

    [Fact]
    public async Task A_sweep_resumes_after_the_anime_the_cursor_names()
    {
        using var host = new SweepHost(Anime(count: 3));

        var cursor = await host.Provider.SweepAsync("1002", TestContext.Current.CancellationToken);

        Assert.Null(cursor);
        var lookup = Assert.Single(host.Requests, uri => uri.Contains("Command=ProgLookup", StringComparison.Ordinal));
        // Only the third anime's title ID is left to ask about.
        Assert.Contains("TID=1003&", lookup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sweep_with_nothing_left_to_walk_ends_at_once()
    {
        using var host = new SweepHost(Anime(count: 3));

        var cursor = await host.Provider.SweepAsync("9000", TestContext.Current.CancellationToken);

        Assert.Null(cursor);
        Assert.Empty(host.Requests);
    }

    [Fact]
    public async Task An_unreadable_cursor_starts_the_sweep_over_rather_than_ending_it()
    {
        using var host = new SweepHost(Anime(count: 3));

        var cursor = await host.Provider.SweepAsync("not-an-anime-id", TestContext.Current.CancellationToken);

        Assert.Null(cursor);
        var lookup = Assert.Single(host.Requests, uri => uri.Contains("Command=ProgLookup", StringComparison.Ordinal));
        Assert.Contains("TID=1001%2C1002%2C1003&", lookup, StringComparison.Ordinal);
        Assert.Contains(LogLevel.Warning, host.Logger.Entries);
    }

    #endregion

    #region Deadlines

    [Fact]
    public async Task A_chunk_whose_budget_is_already_spent_hands_its_cursor_straight_back()
    {
        using var host = new SweepHost(Anime(count: 3));
        using var deadline = new CancellationTokenSource();
        await deadline.CancelAsync();

        var cursor = await host.Provider.SweepAsync("42", deadline.Token);

        Assert.Equal("42", cursor);
        Assert.Empty(host.Requests);
    }

    [Fact]
    public async Task A_chunk_that_runs_out_of_budget_resumes_at_the_last_batch_it_finished()
    {
        using var deadline = new CancellationTokenSource();
        // The first lookup covers a hundred titles and is answered; the
        // deadline fires during the second, the way it fires mid-request on a
        // slow source.
        using var host = new SweepHost(Anime(TwoBatches), (uri, requests) =>
        {
            var lookups = requests.Count(other => other.Contains("Command=ProgLookup", StringComparison.Ordinal));
            if (!uri.Contains("Command=ProgLookup", StringComparison.Ordinal) || lookups < 2)
                return false;

            deadline.Cancel();
            return true;
        });

        var cursor = await host.Provider.SweepAsync(null, deadline.Token);

        // The hundredth anime, so the work the first batch did is kept.
        Assert.Equal("1100", cursor);
    }

    [Fact]
    public async Task The_chunk_after_a_spent_budget_carries_on_from_the_cursor()
    {
        using var host = new SweepHost(Anime(TwoBatches));

        var cursor = await host.Provider.SweepAsync("1100", TestContext.Current.CancellationToken);

        Assert.Null(cursor);
        var lookup = Assert.Single(host.Requests, uri => uri.Contains("Command=ProgLookup", StringComparison.Ordinal));
        Assert.Contains("TID=1101%2C", lookup, StringComparison.Ordinal);
        Assert.DoesNotContain("TID=1001%2C", lookup, StringComparison.Ordinal);
    }

    #endregion

    #region Logging

    [Fact]
    public async Task A_sweep_says_nothing_at_information_level()
    {
        using var host = new SweepHost(Anime(count: 3));

        await host.Provider.SweepAsync(null, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(LogLevel.Information, host.Logger.Entries);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Anime numbered from 1001 up, each carrying the Syoboi title ID of the
    /// same number, so a request's <c>TID</c> list says which anime a chunk
    /// covered.
    /// </summary>
    /// <param name="count">How many anime to build.</param>
    /// <returns>The anime.</returns>
    private static IReadOnlyList<IAnidbAnime> Anime(int count)
        => [.. Enumerable.Range(1001, count).Select(id => Host.AnidbAnime(id, syoboiTitleId: id))];

    /// <summary>
    /// The provider, wired to a stubbed cal.syoboi.jp and a metadata service
    /// holding a fixed set of anime.
    /// </summary>
    private sealed class SweepHost : IDisposable
    {
        private readonly StubSyoboiHandler _handler;
        private readonly HttpClient _httpClient;

        public RecordingLogger<SyoboiAiringScheduleProvider> Logger { get; } = new();

        public SyoboiAiringScheduleProvider Provider { get; }

        public IReadOnlyList<string> Requests => _handler.Requests;

        public SweepHost(IReadOnlyList<IAnidbAnime> anime, Func<string, IReadOnlyList<string>, bool>? abortWhen = null)
        {
            _handler = new StubSyoboiHandler(abortWhen);
            _httpClient = new HttpClient(_handler) { BaseAddress = new Uri(SyoboiConstants.BaseUrl) };
            var apiClient = new SyoboiApiClient(
                _httpClient,
                new SyoboiRateLimiter(minInterval: TimeSpan.Zero),
                new SyoboiChannelDirectoryCache(),
                NullLogger<SyoboiApiClient>.Instance
            );
            Provider = new SyoboiAiringScheduleProvider(
                Logger,
                Host.ConfigurationProvider(new Configuration { ActiveOnly = false }),
                // None of these anime has an episode for an airing to be
                // pinned to, so the sweep never reaches a write.
                airingScheduleService: null!,
                Host.MetadataService(anime),
                apiClient
            );
        }

        public void Dispose()
        {
            _httpClient.Dispose();
            _handler.Dispose();
        }
    }

    #endregion
}
