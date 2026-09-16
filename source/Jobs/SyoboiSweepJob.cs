using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shoko.Abstractions.Config;
using Shoko.Abstractions.Metadata.Anidb;
using Shoko.Abstractions.Metadata.Services;
using Shoko.Plugin.Syoboi.Http;
using Shoko.Plugin.Syoboi.Mapping;
using Shoko.QueueProcessor.Abstractions;
using Shoko.QueueProcessor.Acquisition.Attributes;
using Shoko.QueueProcessor.Concurrency;

namespace Shoko.Plugin.Syoboi.Jobs;

/// <summary>
/// Periodic sweep of every locally known AniDB anime with a Syoboi title ID,
/// fetched in one request as the airing schedule plan asks providers to:
/// "Syoboi sweeps every tracked title in one weekly request." Registered as
/// a recurring job from <c>Plugin.RegisterServices(IApplicationBuilder, IApplicationPaths)</c>.
/// </summary>
/// <remarks>
/// <c>[DatabaseRequired]</c> and <c>[NetworkRequired]</c> hold the job out of
/// the worker pool until the database is ready and connectivity is
/// confirmed. <c>[DisallowConcurrentExecution]</c> keeps a slow sweep from
/// overlapping the next scheduled one.
/// </remarks>
[DatabaseRequired]
[NetworkRequired]
[DisallowConcurrentExecution]
public sealed class SyoboiSweepJob : IQueueJob
{
    // Spreads sweeps started at the same moment across many installs (e.g.
    // everyone restarting a container image at the top of the hour) over a
    // five-minute window, as the plan asks distributed tools to do.
    private static readonly TimeSpan MaxStartJitter = TimeSpan.FromMinutes(5);

    private readonly ILogger<SyoboiSweepJob> _logger;
    private readonly IMetadataService _metadataService;
    private readonly ConfigurationProvider<Configuration> _configurationProvider;
    private readonly SyoboiAiringScheduleProvider _provider;
    private readonly SyoboiApiClient _apiClient;
    private readonly TimeProvider _timeProvider;

    /// <inheritdoc/>
    public string TypeName => "Syoboi Calendar Sweep";

    /// <inheritdoc/>
    public string Title => "Sweeping tracked anime against Syoboi Calendar...";

    /// <summary>
    /// Initializes a new instance of the <see cref="SyoboiSweepJob"/> class.
    /// </summary>
    public SyoboiSweepJob(
        ILogger<SyoboiSweepJob> logger,
        IMetadataService metadataService,
        ConfigurationProvider<Configuration> configurationProvider,
        SyoboiAiringScheduleProvider provider,
        SyoboiApiClient apiClient,
        TimeProvider? timeProvider = null)
    {
        _logger = logger;
        _metadataService = metadataService;
        _configurationProvider = configurationProvider;
        _provider = provider;
        _apiClient = apiClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public async Task Process()
    {
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * MaxStartJitter.TotalMilliseconds);
        if (jitter > TimeSpan.Zero)
            await Task.Delay(jitter, _timeProvider, CancellationToken.None).ConfigureAwait(false);

        var config = _configurationProvider.Load();
        var candidates = FindCandidates(config).ToList();
        if (candidates.Count == 0)
        {
            _logger.LogDebug("Syoboi Calendar sweep found no tracked anime.");
            return;
        }

        _logger.LogInformation("Sweeping {Count} anime against Syoboi Calendar in one request.", candidates.Count);

        var lookup = await _apiClient.ProgLookupAsync([.. candidates.Select(c => c.TitleId)]).ConfigureAwait(false);

        var updated = 0;
        foreach (var (anime, titleId) in candidates)
        {
            if (_provider.ApplyLookupResult(anime, titleId, lookup))
                updated++;
        }

        _logger.LogInformation("Syoboi Calendar sweep wrote schedules for {Updated}/{Count} anime.", updated, candidates.Count);
    }

    private IEnumerable<(IAnidbAnime Anime, int TitleId)> FindCandidates(Configuration config)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var upcomingCutoff = now.AddDays(config.UpcomingWindowDays);
        var endedCutoff = now.AddDays(-config.RecentlyEndedWindowDays);

        foreach (var series in _metadataService.GetAllSeriesForProvider(IMetadataService.ProviderName.AniDB))
        {
            if (series is not IAnidbAnime anime)
                continue;

            if (!SyoboiTitleIdResolver.TryGetTitleId(anime.Resources, out var titleId))
                continue;

            if (config.ActiveOnly && !IsRelevant(anime, upcomingCutoff, endedCutoff))
                continue;

            yield return (anime, titleId);
        }
    }

    private static bool IsRelevant(IAnidbAnime anime, DateTime upcomingCutoff, DateTime endedCutoff)
    {
        // No known air date at all: keep it, since we have nothing to filter
        // on and it's cheap to let one extra title ride along in the batch.
        if (anime.AirDate is not { } airDate)
            return true;

        if (airDate.ToDateTime() > upcomingCutoff)
            return false;

        var effectiveEnd = anime.EndDate?.ToDateTime();
        return effectiveEnd is null || effectiveEnd.Value >= endedCutoff;
    }
}
