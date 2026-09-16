using System;
using System.Threading;
using System.Threading.Tasks;
using Shoko.Plugin.Syoboi.Models;

namespace Shoko.Plugin.Syoboi.Http;

/// <summary>
/// Holds Syoboi's channel directory between requests. The directory is
/// site-wide rather than per-title, is a few hundred rows, and changes at most
/// a handful of times a year, so fetching it once and reusing it saves two
/// requests per sweep against a small community-run service.
/// </summary>
/// <remarks>
/// Registered as a singleton because <see cref="SyoboiApiClient"/> is a typed
/// <c>HttpClient</c> and therefore transient.
/// </remarks>
public sealed class SyoboiChannelDirectoryCache
{
    /// <summary>
    /// How long a fetched directory is reused before it is fetched again.
    /// </summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(24);

    private sealed record Entry(SyoboiChannelDirectory Directory, DateTimeOffset FetchedAt);

    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _lifetime;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Entry? _entry;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyoboiChannelDirectoryCache"/> class.
    /// </summary>
    /// <param name="timeProvider">Optional. The time provider to use. Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="lifetime">Optional. How long an entry stays fresh. Defaults to <see cref="DefaultLifetime"/>.</param>
    public SyoboiChannelDirectoryCache(TimeProvider? timeProvider = null, TimeSpan? lifetime = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lifetime = lifetime ?? DefaultLifetime;
    }

    /// <summary>
    /// Returns the cached directory, fetching it first if there isn't a fresh
    /// one. Concurrent callers share a single fetch.
    /// </summary>
    /// <param name="fetch">
    /// Fetches the directory. It may answer <c>null</c> or an empty directory
    /// when the request failed, which is not cached.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// The directory, or <see cref="SyoboiChannelDirectory.Empty"/> if it
    /// could not be fetched.
    /// </returns>
    public async Task<SyoboiChannelDirectory> GetOrFetchAsync(Func<CancellationToken, Task<SyoboiChannelDirectory?>> fetch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fetch);

        if (TryGet(out var cached))
            return cached;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another caller may have filled the cache while we waited.
            if (TryGet(out cached))
                return cached;

            var fetched = await fetch(cancellationToken).ConfigureAwait(false);
            if (fetched is null || fetched.IsEmpty)
                return SyoboiChannelDirectory.Empty;

            Volatile.Write(ref _entry, new Entry(fetched, _timeProvider.GetUtcNow()));
            return fetched;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Returns the cached directory, if there is a fresh one.
    /// </summary>
    /// <param name="directory">The cached directory, when one is fresh.</param>
    /// <returns><c>true</c> when a fresh directory was cached.</returns>
    public bool TryGet(out SyoboiChannelDirectory directory)
    {
        if (Volatile.Read(ref _entry) is { } entry && _timeProvider.GetUtcNow() - entry.FetchedAt < _lifetime)
        {
            directory = entry.Directory;
            return true;
        }

        directory = SyoboiChannelDirectory.Empty;
        return false;
    }

    /// <summary>
    /// Drops whatever is cached, so the next caller fetches a fresh directory.
    /// </summary>
    public void Clear()
        => Volatile.Write(ref _entry, null);
}
