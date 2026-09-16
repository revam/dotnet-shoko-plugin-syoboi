using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shoko.Plugin.Syoboi.Models;

namespace Shoko.Plugin.Syoboi.Http;

/// <summary>
/// Talks to cal.syoboi.jp's <c>db.php</c> JSON API, rate-limited to one
/// request per second (see <see cref="SyoboiRateLimiter"/>). The
/// <c>HttpClient</c> is registered with <c>AddHttpClient</c> in
/// <c>Plugin.RegisterServices(IServiceCollection, IApplicationPaths)</c>,
/// which is where the custom User-Agent header is set.
/// </summary>
public sealed class SyoboiApiClient
{
    /// <summary>
    /// The default lookup range: a couple of weeks either side of "now",
    /// wide enough to catch a delayed broadcast without asking Syoboi for a
    /// title's entire history every sweep.
    /// </summary>
    public const string DefaultRange = "thisweek2";

    private readonly HttpClient _httpClient;
    private readonly SyoboiRateLimiter _rateLimiter;
    private readonly ILogger<SyoboiApiClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyoboiApiClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client, with its base address and User-Agent already configured.</param>
    /// <param name="rateLimiter">The rate limiter to use.</param>
    /// <param name="logger">The logger.</param>
    public SyoboiApiClient(HttpClient httpClient, SyoboiRateLimiter rateLimiter, ILogger<SyoboiApiClient> logger)
    {
        _httpClient = httpClient;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    /// <summary>
    /// Looks up broadcast schedules for one or more Syoboi title IDs in a
    /// single request, along with the channels and channel groups needed to
    /// make sense of them.
    /// </summary>
    /// <param name="titleIds">The Syoboi title IDs to look up. One request covers all of them.</param>
    /// <param name="range">Optional. The Syoboi date range keyword. Defaults to <see cref="DefaultRange"/>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// The parsed result, or <see cref="SyoboiLookupResult.Empty"/> if
    /// <paramref name="titleIds"/> is empty or the request fails.
    /// </returns>
    public async Task<SyoboiLookupResult> ProgLookupAsync(IReadOnlyCollection<int> titleIds, string range = DefaultRange, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(titleIds);

        if (titleIds.Count == 0)
            return SyoboiLookupResult.Empty;

        var tidParam = string.Join(",", titleIds.Distinct().OrderBy(id => id));
        var requestUri = $"{SyoboiConstants.LookupPath}?Command=ProgLookup&TID={Uri.EscapeDataString(tidParam)}&Range={Uri.EscapeDataString(range)}&ChID=*&JSON";

        await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return SyoboiResponseParser.Parse(json, _logger);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to reach Syoboi Calendar for {Count} title(s).", titleIds.Count);
            return SyoboiLookupResult.Empty;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Timed out reaching Syoboi Calendar for {Count} title(s).", titleIds.Count);
            return SyoboiLookupResult.Empty;
        }
    }
}
