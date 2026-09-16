using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shoko.Plugin.Syoboi.Mapping;
using Shoko.Plugin.Syoboi.Models;

namespace Shoko.Plugin.Syoboi.Http;

/// <summary>
/// Talks to cal.syoboi.jp's <c>db.php</c> XML API, rate-limited to one
/// request per second (see <see cref="SyoboiRateLimiter"/>). The
/// <c>HttpClient</c> is registered with <c>AddHttpClient</c> in
/// <c>Plugin.RegisterServices(IServiceCollection, IApplicationPaths)</c>,
/// which is where the custom User-Agent header is set.
/// </summary>
/// <remarks>
/// Every command is a request of its own: broadcast slots come from
/// <c>ProgLookup</c>, and the channels they refer to from <c>ChLookup</c> plus
/// <c>ChGroupLookup</c>, which are cached site-wide by
/// <see cref="SyoboiChannelDirectoryCache"/>.
/// </remarks>
public sealed class SyoboiApiClient
{
    /// <summary>
    /// How far back a lookup reaches when no window is given. Wide enough to
    /// pick up a slot that was pushed back after the last sweep saw it.
    /// </summary>
    public static readonly TimeSpan DefaultLookback = TimeSpan.FromDays(14);

    /// <summary>
    /// How far ahead a lookup reaches when no window is given. Syoboi's
    /// volunteers fill the schedule in a few weeks ahead of broadcast.
    /// </summary>
    public static readonly TimeSpan DefaultLookahead = TimeSpan.FromDays(28);

    /// <summary>
    /// How many title IDs one <c>ProgLookup</c> request asks about. Syoboi
    /// takes a comma-separated list of any length, but a sweep of a large
    /// library would otherwise build a query string thousands of characters
    /// long, and answers are capped at
    /// <see cref="SyoboiConstants.MaxProgramsPerRequest"/> rows regardless.
    /// </summary>
    public const int MaxTitleIdsPerRequest = 100;

    private const string ProgramFields = "PID,TID,ChID,StTime,StOffset,EdTime,Count,Flag,Deleted";

    private const string ChannelFields = "ChID,ChName,ChiEPGName,ChGID";

    private const string TitleFields = "TID,Title,ShortTitle,TitleYomi,TitleEN,Cat";

    private readonly HttpClient _httpClient;
    private readonly SyoboiRateLimiter _rateLimiter;
    private readonly SyoboiChannelDirectoryCache _channelCache;
    private readonly ILogger<SyoboiApiClient> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyoboiApiClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client, with its base address and User-Agent already configured.</param>
    /// <param name="rateLimiter">The rate limiter to use.</param>
    /// <param name="channelCache">The shared channel directory cache.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="timeProvider">Optional. The time provider to use. Defaults to <see cref="TimeProvider.System"/>.</param>
    public SyoboiApiClient(
        HttpClient httpClient,
        SyoboiRateLimiter rateLimiter,
        SyoboiChannelDirectoryCache channelCache,
        ILogger<SyoboiApiClient> logger,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient;
        _rateLimiter = rateLimiter;
        _channelCache = channelCache;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    #region Programs

    /// <summary>
    /// Looks up broadcast slots for one or more Syoboi title IDs, along with
    /// the channels and channel groups needed to make sense of them.
    /// </summary>
    /// <param name="titleIds">
    /// The Syoboi title IDs to look up. They are batched
    /// <see cref="MaxTitleIdsPerRequest"/> at a time.
    /// </param>
    /// <param name="fromUtc">
    /// Optional. The start of the window to look up, in UTC. Defaults to
    /// <see cref="DefaultLookback"/> ago.
    /// </param>
    /// <param name="toUtc">
    /// Optional. The end of the window, in UTC. Defaults to
    /// <see cref="DefaultLookahead"/> from now. Syoboi requires both bounds.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// The parsed result, or <see cref="SyoboiLookupResult.Empty"/> if
    /// <paramref name="titleIds"/> is empty, the channel directory could not
    /// be fetched, or every request failed.
    /// </returns>
    public async Task<SyoboiLookupResult> ProgLookupAsync(
        IReadOnlyCollection<int> titleIds,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(titleIds);

        if (titleIds.Count is 0)
            return SyoboiLookupResult.Empty;

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var from = fromUtc ?? now - DefaultLookback;
        var to = toUtc ?? now + DefaultLookahead;
        if (to <= from)
        {
            _logger.LogWarning("Refusing to look up Syoboi Calendar for an empty window ({From} — {To}).", from, to);
            return SyoboiLookupResult.Empty;
        }

        var directory = await GetChannelDirectoryAsync(cancellationToken).ConfigureAwait(false);
        if (directory.IsEmpty)
        {
            _logger.LogWarning("Skipping the Syoboi Calendar lookup: the channel directory is unavailable.");
            return SyoboiLookupResult.Empty;
        }

        var range = SyoboiTimeConverter.ToRange(from, to);
        var programs = new List<SyoboiProgramEntry>();
        _logger.LogDebug("Looking up {Count} Syoboi title(s) over {Range}.", titleIds.Count, range);
        foreach (var batch in Batch(titleIds, MaxTitleIdsPerRequest))
        {
            var requestUri = BuildRequestUri("ProgLookup", ("TID", string.Join(",", batch)), ("Range", range), ("Fields", ProgramFields));
            if (await GetAsync(requestUri, cancellationToken).ConfigureAwait(false) is not { } body)
                continue;

            var response = SyoboiResponseParser.ParsePrograms(body, _logger);
            if (response.IsError)
            {
                _logger.LogWarning("Syoboi Calendar could not look up {Count} title(s): {Code} {Message}", batch.Count, response.Code, response.Message);
                continue;
            }

            programs.AddRange(response.Value);
            if (response.Value.Count >= SyoboiConstants.MaxProgramsPerRequest)
                _logger.LogWarning("A Syoboi Calendar lookup hit the {Limit} row limit; narrow the window or the batch to see the rest.", SyoboiConstants.MaxProgramsPerRequest);
        }

        _logger.LogDebug("Syoboi Calendar returned {Count} broadcast slot(s) for {Titles} title(s).", programs.Count, titleIds.Count);
        return new SyoboiLookupResult(programs, directory);
    }

    #endregion

    #region Channels

    /// <summary>
    /// Returns Syoboi's channel directory, fetching it if there isn't a fresh
    /// one cached.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// The directory, or <see cref="SyoboiChannelDirectory.Empty"/> if it
    /// could not be fetched.
    /// </returns>
    public Task<SyoboiChannelDirectory> GetChannelDirectoryAsync(CancellationToken cancellationToken = default)
        => _channelCache.GetOrFetchAsync(FetchChannelDirectoryAsync, cancellationToken);

    private async Task<SyoboiChannelDirectory?> FetchChannelDirectoryAsync(CancellationToken cancellationToken)
    {
        var channelUri = BuildRequestUri("ChLookup", ("Fields", ChannelFields));
        if (await GetAsync(channelUri, cancellationToken).ConfigureAwait(false) is not { } channelBody)
            return null;

        var channels = SyoboiResponseParser.ParseChannels(channelBody, _logger);
        if (channels.IsError)
        {
            _logger.LogWarning("Syoboi Calendar could not list its channels: {Code} {Message}", channels.Code, channels.Message);
            return null;
        }

        var groupUri = BuildRequestUri("ChGroupLookup");
        if (await GetAsync(groupUri, cancellationToken).ConfigureAwait(false) is not { } groupBody)
            return null;

        var groups = SyoboiResponseParser.ParseChannelGroups(groupBody, _logger);
        if (groups.IsError)
        {
            _logger.LogWarning("Syoboi Calendar could not list its channel groups: {Code} {Message}", groups.Code, groups.Message);
            return null;
        }

        _logger.LogDebug("Fetched {Channels} Syoboi channel(s) in {Groups} group(s).", channels.Value.Count, groups.Value.Count);
        return new SyoboiChannelDirectory(channels.Value, groups.Value);
    }

    #endregion

    #region Titles

    /// <summary>
    /// Looks up Syoboi's own title records, e.g. to confirm what a title ID
    /// points at.
    /// </summary>
    /// <param name="titleIds">
    /// The Syoboi title IDs to look up. They are batched
    /// <see cref="MaxTitleIdsPerRequest"/> at a time.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The titles that were found, keyed by <see cref="SyoboiTitle.TID"/>.</returns>
    public async Task<IReadOnlyDictionary<int, SyoboiTitle>> TitleLookupAsync(IReadOnlyCollection<int> titleIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(titleIds);

        var titles = new Dictionary<int, SyoboiTitle>();
        if (titleIds.Count is 0)
            return titles;

        foreach (var batch in Batch(titleIds, MaxTitleIdsPerRequest))
        {
            var requestUri = BuildRequestUri("TitleLookup", ("TID", string.Join(",", batch)), ("Fields", TitleFields));
            if (await GetAsync(requestUri, cancellationToken).ConfigureAwait(false) is not { } body)
                continue;

            var response = SyoboiResponseParser.ParseTitles(body, _logger);
            if (response.IsError)
            {
                _logger.LogWarning("Syoboi Calendar could not look up {Count} title(s): {Code} {Message}", batch.Count, response.Code, response.Message);
                continue;
            }

            foreach (var (titleId, title) in response.Value)
                titles[titleId] = title;
        }

        return titles;
    }

    #endregion

    #region Requests

    private static string BuildRequestUri(string command, params (string Name, string Value)[] parameters)
    {
        var query = string.Join("&", parameters.Select(parameter => $"{parameter.Name}={Uri.EscapeDataString(parameter.Value)}"));
        return query.Length is 0
            ? $"{SyoboiConstants.DatabasePath}?Command={command}"
            : $"{SyoboiConstants.DatabasePath}?Command={command}&{query}";
    }

    private async Task<string?> GetAsync(string requestUri, CancellationToken cancellationToken)
    {
        await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to reach Syoboi Calendar at {RequestUri}.", requestUri);
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Timed out reaching Syoboi Calendar at {RequestUri}.", requestUri);
            return null;
        }
    }

    private static IEnumerable<IReadOnlyList<int>> Batch(IReadOnlyCollection<int> titleIds, int size)
        => titleIds.Distinct().Order().Chunk(size);

    #endregion
}
