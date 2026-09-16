using System;
using System.Collections.Generic;
using System.Globalization;
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
/// Periodic sweep of every locally known AniDB anime with a Syoboi title ID.
/// Registered as a recurring job from
/// <c>Plugin.RegisterServices(IApplicationBuilder, IApplicationPaths)</c>.
/// </summary>
/// <remarks>
/// <para>
/// One execution covers at most <see cref="MaxAnimePerRun"/> anime and then
/// enqueues itself for the next slice, rather than walking the whole library
/// in one go: at one request per second a large library would otherwise hold
/// a worker for hours and trip the queue watchdog. Each slice is a handful of
/// requests — Syoboi takes a comma-separated list of title IDs, so
/// <see cref="SyoboiApiClient.MaxTitleIdsPerRequest"/> anime share one
/// request — and the channel directory is fetched once a day and shared
/// across every slice by <see cref="SyoboiChannelDirectoryCache"/>.
/// </para>
/// <para>
/// <c>[DatabaseRequired]</c> and <c>[NetworkRequired]</c> hold the job out of
/// the worker pool until the database is ready and connectivity is
/// confirmed. <c>[DisallowConcurrentExecution]</c> keeps a slow slice from
/// overlapping the next one.
/// </para>
/// </remarks>
[DatabaseRequired]
[NetworkRequired]
[DisallowConcurrentExecution]
public sealed class SyoboiSweepJob : IQueueJob
{
    /// <summary>
    /// How many anime one execution covers. Five
    /// <see cref="SyoboiApiClient.MaxTitleIdsPerRequest"/>-sized requests at
    /// one request per second, so a slice takes seconds rather than the
    /// hours a whole-library walk would.
    /// </summary>
    public const int MaxAnimePerRun = 500;

    // Spreads sweeps started at the same moment across many installs (e.g.
    // everyone restarting a container image at the top of the hour) over a
    // five-minute window, as the plan asks distributed tools to do. Only the
    // first slice waits; the continuations are already spread out by it.
    private static readonly TimeSpan _maxStartJitter = TimeSpan.FromMinutes(5);

    private readonly ILogger<SyoboiSweepJob> _logger;
    private readonly IMetadataService _metadataService;
    private readonly ConfigurationProvider<Configuration> _configurationProvider;
    private readonly SyoboiAiringScheduleProvider _provider;
    private readonly SyoboiApiClient _apiClient;
    private readonly IQueueScheduler _scheduler;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Only anime with an AniDB ID above this are swept, so each execution
    /// picks up where the last one left off. <c>0</c> starts a fresh sweep,
    /// which is what the recurring registration enqueues.
    /// </summary>
    public int AfterAnidbAnimeID { get; set; }

    /// <inheritdoc/>
    public string TypeName => "Syoboi Calendar Sweep";

    /// <inheritdoc/>
    public string Title => "Sweeping tracked anime against Syoboi Calendar...";

    /// <inheritdoc/>
    public IDictionary<string, object> Details => AfterAnidbAnimeID is 0
        ? new Dictionary<string, object>()
        : new Dictionary<string, object> { { "After AniDB Anime ID", AfterAnidbAnimeID.ToString(CultureInfo.InvariantCulture) } };

    /// <summary>
    /// Initializes a new instance of the <see cref="SyoboiSweepJob"/> class.
    /// </summary>
    public SyoboiSweepJob(
        ILogger<SyoboiSweepJob> logger,
        IMetadataService metadataService,
        ConfigurationProvider<Configuration> configurationProvider,
        SyoboiAiringScheduleProvider provider,
        SyoboiApiClient apiClient,
        IQueueScheduler scheduler,
        TimeProvider? timeProvider = null)
    {
        _logger = logger;
        _metadataService = metadataService;
        _configurationProvider = configurationProvider;
        _provider = provider;
        _apiClient = apiClient;
        _scheduler = scheduler;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public async Task Process()
    {
        if (AfterAnidbAnimeID is 0)
        {
            var jitter = TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * _maxStartJitter.TotalMilliseconds);
            if (jitter > TimeSpan.Zero)
                await Task.Delay(jitter, _timeProvider, CancellationToken.None).ConfigureAwait(false);
        }

        var config = _configurationProvider.Load();
        var candidates = FindCandidates(config)
            .Where(candidate => candidate.Anime.ID > AfterAnidbAnimeID)
            .OrderBy(candidate => candidate.Anime.ID)
            .Take(MaxAnimePerRun)
            .ToList();
        if (candidates.Count is 0)
        {
            _logger.LogDebug("Syoboi Calendar sweep found no more tracked anime after AniDB anime {AnimeID}.", AfterAnidbAnimeID);
            return;
        }

        _logger.LogInformation("Sweeping {Count} anime against Syoboi Calendar, starting after AniDB anime {AnimeID}.", candidates.Count, AfterAnidbAnimeID);

        var lookup = await _apiClient.ProgLookupAsync([.. candidates.Select(candidate => candidate.TitleId)]).ConfigureAwait(false);

        var updated = 0;
        foreach (var (anime, titleId) in candidates)
        {
            if (_provider.ApplyLookupResult(anime, titleId, lookup))
                updated++;
        }

        _logger.LogInformation("Syoboi Calendar sweep wrote schedules for {Updated}/{Count} anime.", updated, candidates.Count);

        // A full slice means there is probably more to do; the run that finds
        // nothing left is the one that ends the sweep, and costs no requests.
        if (candidates.Count < MaxAnimePerRun)
            return;

        var lastAnimeId = candidates[^1].Anime.ID;
        await _scheduler.Enqueue<SyoboiSweepJob>(job => job.AfterAnidbAnimeID = lastAnimeId).ConfigureAwait(false);
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
