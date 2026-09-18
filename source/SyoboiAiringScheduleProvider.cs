using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shoko.Abstractions.Config;
using Shoko.Abstractions.Metadata;
using Shoko.Abstractions.Metadata.Airing;
using Shoko.Abstractions.Metadata.Anidb;
using Shoko.Abstractions.Metadata.Enums;
using Shoko.Abstractions.Metadata.Services;
using Shoko.Abstractions.Metadata.Shoko;
using Shoko.Plugin.Syoboi.Http;
using Shoko.Plugin.Syoboi.Mapping;
using Shoko.Plugin.Syoboi.Models;

namespace Shoko.Plugin.Syoboi;

/// <summary>
/// Airing schedule provider for cal.syoboi.jp (Syoboi Calendar): Japanese TV
/// and streaming broadcast slots, keyed to AniDB anime through their Syoboi
/// title ID.
/// </summary>
/// <remarks>
/// <para>
/// Only <see cref="AiringKind.Original"/> is ever produced: Syoboi tracks the
/// original Japanese broadcast/stream, not subtitled or dubbed releases.
/// </para>
/// <para>
/// Every lookup asks Syoboi about a window rather than a whole run, so every
/// write is a delta through <c>MergeAirings</c>: a slot inside that window
/// which Syoboi no longer lists is named as a removal, and everything outside
/// it is left exactly as it is.
/// </para>
/// </remarks>
public sealed class SyoboiAiringScheduleProvider : IAiringScheduleProvider<Configuration>, ISweepingAiringScheduleProvider
{
    private static readonly IReadOnlySet<AiringKind> _availableKinds = new HashSet<AiringKind> { AiringKind.Original };

    // How far either side of an anime's own air dates a single refresh looks,
    // to catch a premiere moved forward or a finale pushed into the next week.
    private static readonly TimeSpan _runMargin = TimeSpan.FromDays(30);

    // The widest window a single refresh asks for, so a show that has been
    // running since 1999 doesn't ask Syoboi for a quarter century of slots at
    // once (it answers at most 5,000 rows anyway). Long runs are covered from
    // the recent end; everything older is left to a narrower manual refresh.
    private static readonly TimeSpan _maxRunWindow = TimeSpan.FromDays(2 * 365);

    private readonly ILogger<SyoboiAiringScheduleProvider> _logger;
    private readonly ConfigurationProvider<Configuration> _configurationProvider;
    private readonly IAiringScheduleService _airingScheduleService;
    private readonly IMetadataService _metadataService;
    private readonly SyoboiApiClient _apiClient;
    private readonly TimeProvider _timeProvider;

    /// <inheritdoc/>
    public string Name => "Syoboi Calendar";

    /// <inheritdoc/>
    public string? Description => "Japanese TV and streaming broadcast schedules from cal.syoboi.jp.";

    /// <inheritdoc/>
    public IReadOnlySet<AiringKind> AvailableKinds => _availableKinds;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyoboiAiringScheduleProvider"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="configurationProvider">The configuration provider.</param>
    /// <param name="airingScheduleService">The airing schedule service.</param>
    /// <param name="metadataService">The metadata service, used to resolve a Shoko series' AniDB anime.</param>
    /// <param name="apiClient">The Syoboi API client.</param>
    /// <param name="timeProvider">Optional. The time provider to use. Defaults to <see cref="TimeProvider.System"/>.</param>
    public SyoboiAiringScheduleProvider(
        ILogger<SyoboiAiringScheduleProvider> logger,
        ConfigurationProvider<Configuration> configurationProvider,
        IAiringScheduleService airingScheduleService,
        IMetadataService metadataService,
        SyoboiApiClient apiClient,
        TimeProvider? timeProvider = null)
    {
        _logger = logger;
        _configurationProvider = configurationProvider;
        _airingScheduleService = airingScheduleService;
        _metadataService = metadataService;
        _apiClient = apiClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    #region Refreshing

    /// <inheritdoc/>
    public async Task<bool> RefreshAsync(ISeries series, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(series);

        var anime = ResolveAnidbAnime(series);
        if (anime is null)
        {
            _logger.LogDebug("Skipping refresh for {SeriesID}: not an AniDB anime and no linked one was found.", series.ID);
            return false;
        }

        if (!SyoboiTitleIdResolver.TryGetTitleId(anime.Resources, out var titleId))
        {
            _logger.LogDebug("Skipping refresh for AniDB anime {AnimeID}: no Syoboi title ID.", anime.ID);
            return false;
        }

        var (fromUtc, toUtc) = GetLookupWindow(anime, _timeProvider.GetUtcNow().UtcDateTime);
        _logger.LogDebug("Refreshing AniDB anime {AnimeID} from Syoboi title {TitleID}, covering {From:u} — {To:u}.", anime.ID, titleId, fromUtc, toUtc);

        var lookup = await _apiClient.ProgLookupAsync([titleId], fromUtc, toUtc, cancellationToken).ConfigureAwait(false);
        return ApplyLookupResult(anime, titleId, lookup, fromUtc, toUtc);
    }

    /// <summary>
    /// The window to ask Syoboi about for a single anime. A refresh of one
    /// series is cheap enough to cover its whole run, rather than the sweep's
    /// rolling few weeks, so a series that was added late still gets its
    /// earlier slots.
    /// </summary>
    /// <param name="anime">The anime being refreshed.</param>
    /// <param name="now">The current time, in UTC.</param>
    /// <returns>The window, in UTC.</returns>
    private static (DateTime FromUtc, DateTime ToUtc) GetLookupWindow(IAnidbAnime anime, DateTime now)
    {
        var to = anime.EndDate is { } endDate ? Min(endDate.ToDateTime() + _runMargin, now + SyoboiApiClient.DefaultLookahead) : now + SyoboiApiClient.DefaultLookahead;
        var from = anime.AirDate is { } airDate ? Max(airDate.ToDateTime() - _runMargin, to - _maxRunWindow) : to - _maxRunWindow;
        return to > from ? (from, to) : (from, from + _runMargin);

        static DateTime Min(DateTime left, DateTime right) => left < right ? left : right;

        static DateTime Max(DateTime left, DateTime right) => left > right ? left : right;
    }

    #endregion

    #region Sweeping

    /// <summary>
    /// Syoboi's volunteers fill the calendar a few weeks ahead of broadcast,
    /// and the site asks clients to be gentle, so walking every tracked title
    /// once a week is plenty. The value actually used is the user's own
    /// <c>AiringScheduleProviderInfo.SweepInterval</c>, which this only seeds,
    /// and the server never sweeps more often than every fifteen minutes.
    /// </summary>
    public TimeSpan? SuggestedSweepInterval => TimeSpan.FromDays(7);

    /// <inheritdoc/>
    public async Task<string?> SweepAsync(string? cursor, CancellationToken cancellationToken)
    {
        var after = ParseCursor(cursor);
        var config = _configurationProvider.Load();
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var candidates = FindCandidates(config, now)
            .Where(candidate => candidate.Anime.ID > after)
            .OrderBy(candidate => candidate.Anime.ID)
            .ToList();
        if (candidates.Count is 0)
        {
            _logger.LogDebug("The sweep found no tracked anime after AniDB anime {AnimeID}, and has come full circle.", after);
            return null;
        }

        // Syoboi takes a comma-separated list of title IDs, so a chunk costs
        // one request per hundred anime rather than one per anime, and the
        // channel directory behind them is fetched once a day and shared.
        var fromUtc = now - SyoboiApiClient.DefaultLookback;
        var toUtc = now + SyoboiApiClient.DefaultLookahead;
        var swept = 0;
        var written = 0;
        foreach (var batch in candidates.Chunk(SyoboiApiClient.MaxTitleIdsPerRequest))
        {
            if (cancellationToken.IsCancellationRequested)
                return ResumeAfter(after, swept);

            SyoboiLookupResult lookup;
            try
            {
                lookup = await _apiClient
                    .ProgLookupAsync([.. batch.Select(candidate => candidate.TitleId)], fromUtc, toUtc, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The budget ran out mid-request. Handing back the ground
                // already covered beats letting the chunk end as a timeout,
                // which would walk this batch again from the old cursor.
                return ResumeAfter(after, swept);
            }

            foreach (var (anime, titleId) in batch)
            {
                if (ApplyLookupResult(anime, titleId, lookup, fromUtc, toUtc))
                    written++;
            }

            swept += batch.Length;
            after = batch[^1].Anime.ID;
        }

        _logger.LogDebug(
            "The sweep covered the last {Count} tracked anime, wrote schedules for {Written} of them, and has come full circle.",
            swept,
            written
        );
        return null;

        string ResumeAfter(int animeId, int count)
        {
            _logger.LogDebug(
                "The sweep covered {Count} anime before running out of budget; the next chunk resumes after AniDB anime {AnimeID}.",
                count,
                animeId
            );
            return FormatCursor(animeId);
        }
    }

    /// <summary>
    /// Reads the AniDB anime ID the last chunk finished at out of the cursor.
    /// A cursor that cannot be read starts the sweep over rather than ending
    /// it, since an unreadable cursor says nothing about what has been
    /// covered.
    /// </summary>
    /// <param name="cursor">The cursor the chunk was called with.</param>
    /// <returns>The AniDB anime ID to resume after; <c>0</c> starts a fresh sweep.</returns>
    private int ParseCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
            return 0;

        if (int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var animeId))
            return animeId;

        _logger.LogWarning("Starting a fresh sweep: the cursor \"{Cursor}\" is not an AniDB anime ID.", cursor);
        return 0;
    }

    /// <summary>
    /// Writes the AniDB anime ID to resume after as a cursor.
    /// </summary>
    /// <param name="animeId">The AniDB anime ID the chunk finished at.</param>
    /// <returns>The cursor.</returns>
    private static string FormatCursor(int animeId)
        => animeId.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Every locally known AniDB anime that carries a Syoboi title ID, and is
    /// relevant enough to spend a request on.
    /// </summary>
    /// <param name="config">The configuration to filter by.</param>
    /// <param name="now">The current time, in UTC.</param>
    /// <returns>The anime to sweep, each with its Syoboi title ID.</returns>
    private IEnumerable<(IAnidbAnime Anime, int TitleId)> FindCandidates(Configuration config, DateTime now)
    {
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

    /// <summary>
    /// Whether an anime is currently airing, about to air, or recently ended.
    /// </summary>
    /// <param name="anime">The anime to judge.</param>
    /// <param name="upcomingCutoff">The furthest ahead an air date may be.</param>
    /// <param name="endedCutoff">The furthest back an end date may be.</param>
    /// <returns><c>true</c> when the anime is worth sweeping.</returns>
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

    #endregion

    #region Writing

    /// <summary>
    /// Applies an already-fetched lookup result to one AniDB anime, writing
    /// channels, schedules and airings through <c>IAiringScheduleService</c>.
    /// Shared between <see cref="RefreshAsync(ISeries,CancellationToken)"/>
    /// (a single-title request) and
    /// <see cref="SweepAsync(string,CancellationToken)"/> (one request
    /// covering a hundred titles at once).
    /// </summary>
    /// <param name="anime">The AniDB anime the lookup is for.</param>
    /// <param name="titleId">The Syoboi title ID <paramref name="anime"/> was looked up by.</param>
    /// <param name="lookup">The lookup result, which may cover other titles too.</param>
    /// <param name="fromUtc">The start of the window the lookup asked about, in UTC.</param>
    /// <param name="toUtc">The end of the window the lookup asked about, in UTC.</param>
    /// <returns><c>true</c> if at least one schedule was written.</returns>
    private bool ApplyLookupResult(IAnidbAnime anime, int titleId, SyoboiLookupResult lookup, DateTime fromUtc, DateTime toUtc)
    {
        ArgumentNullException.ThrowIfNull(anime);
        ArgumentNullException.ThrowIfNull(lookup);

        var config = _configurationProvider.Load();
        var allowedGroups = config.AllowedChannelGroups is { Count: > 0 }
            ? new HashSet<string>(config.AllowedChannelGroups, StringComparer.OrdinalIgnoreCase)
            : null;

        var episodesByNumber = anime.Episodes
            .Where(episode => episode.Type == EpisodeType.Episode)
            .GroupBy(episode => episode.EpisodeNumber)
            .ToDictionary(group => group.Key, group => group.First().ID);
        var episodesById = anime.Episodes.ToDictionary(episode => episode.ID);
        if (episodesByNumber.Count is 0)
        {
            _logger.LogDebug("Skipping AniDB anime {AnimeID} (Syoboi title {TitleID}): it has no normal episodes to pin an airing to.", anime.ID, titleId);
            return false;
        }

        // A one-off broadcast — a film or a TV special — carries no episode
        // number, which is only usable when there is a single episode for it
        // to be.
        var bundles = SyoboiScheduleMapper.BuildChannelBundles(titleId, lookup, allowedGroups, allowMissingEpisodeNumbers: episodesByNumber.Count is 1);
        if (bundles.Count is 0)
        {
            LogNoUsableSlots(anime, titleId, lookup, allowedGroups);
            return false;
        }

        var timeZone = TimeZoneInfo.TryFindSystemTimeZoneById(SyoboiConstants.TimeZoneId, out var tz) ? tz : null;
        var isFinished = anime.EndDate.HasValue;

        var wroteAny = false;
        foreach (var bundle in bundles)
        {
            if (!TryWriteSchedule(anime, bundle, timeZone, isFinished, episodesByNumber, episodesById, fromUtc, toUtc))
                continue;

            wroteAny = true;
        }

        if (!wroteAny)
            _logger.LogDebug("Wrote no schedule for AniDB anime {AnimeID} (Syoboi title {TitleID}): none of its {Count} channel(s) had a slot that mapped onto a known episode.", anime.ID, titleId, bundles.Count);

        return wroteAny;
    }

    /// <summary>
    /// Says why a title Syoboi knows about produced nothing to write. A
    /// refresh that quietly answers <c>false</c> is indistinguishable from a
    /// broken provider, so every reason is logged.
    /// </summary>
    /// <param name="anime">The AniDB anime the lookup was for.</param>
    /// <param name="titleId">The Syoboi title ID it was looked up by.</param>
    /// <param name="lookup">The lookup result.</param>
    /// <param name="allowedGroups">The configured channel group allow-list, if any.</param>
    private void LogNoUsableSlots(IAnidbAnime anime, int titleId, SyoboiLookupResult lookup, IReadOnlySet<string>? allowedGroups)
    {
        var slots = lookup.Programs.Where(entry => entry.TID == titleId).ToList();
        if (slots.Count is 0)
        {
            _logger.LogDebug("Syoboi title {TitleID} (AniDB anime {AnimeID}) has no broadcast slots in the requested window.", titleId, anime.ID);
            return;
        }

        var deleted = slots.Count(entry => entry.Deleted);
        var reruns = slots.Count(entry => !entry.Deleted && entry.IsRerun);
        var unnumbered = slots.Count(entry => !entry.Deleted && !entry.IsRerun && entry.Count is null);
        _logger.LogDebug(
            "None of the {Count} slot(s) Syoboi has for title {TitleID} (AniDB anime {AnimeID}) could be used: {Deleted} retracted, {Reruns} rerun(s), {Unnumbered} without an episode number, and the rest on a channel that is unknown, radio{AllowList}.",
            slots.Count, titleId, anime.ID, deleted, reruns, unnumbered, allowedGroups is { Count: > 0 } ? ", or outside the allowed channel groups" : string.Empty
        );
    }

    /// <summary>
    /// Writes one channel's worth of a title's slots as a schedule and its
    /// airings.
    /// </summary>
    /// <param name="anime">The AniDB anime the schedule is for.</param>
    /// <param name="bundle">The channel's surviving slots.</param>
    /// <param name="timeZone">The schedule's display time zone.</param>
    /// <param name="isFinished">Whether the anime's run has ended.</param>
    /// <param name="episodesByNumber">The anime's normal-episode IDs, keyed by episode number.</param>
    /// <param name="episodesById">The anime's episodes, keyed by AniDB episode ID.</param>
    /// <param name="fromUtc">The start of the window the lookup asked about, in UTC.</param>
    /// <param name="toUtc">The end of the window the lookup asked about, in UTC.</param>
    /// <returns><c>true</c> if the schedule was written.</returns>
    private bool TryWriteSchedule(
        IAnidbAnime anime,
        SyoboiChannelBundle bundle,
        TimeZoneInfo? timeZone,
        bool isFinished,
        IReadOnlyDictionary<int, int> episodesByNumber,
        IReadOnlyDictionary<int, IAnidbEpisode> episodesById,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var drafts = SyoboiScheduleMapper.BuildEpisodeDrafts(bundle.Programs, episodesByNumber);
        if (drafts.Count is 0)
        {
            _logger.LogDebug(
                "No slot on Syoboi channel {ChannelID} ({ChannelName}) maps onto a known episode of AniDB anime {AnimeID}; its {Count} slot(s) are numbered {Numbers}.",
                bundle.ChID, bundle.ChannelName, anime.ID, bundle.Programs.Count, string.Join(", ", bundle.Programs.Select(entry => entry.Count?.ToString(CultureInfo.InvariantCulture) ?? "?").Distinct())
            );
            return false;
        }

        var channel = _airingScheduleService.FindOrRegisterChannel(bundle.ChannelName, bundle.ChannelType);
        if (bundle.ChannelAliases.Count > 0)
        {
            try
            {
                _airingScheduleService.AddChannelAliases(channel, bundle.ChannelAliases);
            }
            catch (ChannelAliasConflictException ex)
            {
                // Another channel already claims this EPG name as its own
                // name or alias. That's a naming collision worth knowing
                // about, but not one that should stop this channel's
                // schedule from being written.
                _logger.LogWarning(ex, "Could not add alias(es) to channel {ChannelID} ({ChannelName}).", channel.ID, channel.Name);
            }
        }

        var scheduleData = new AiringScheduleData
        {
            Series = anime,
            ChannelID = channel.ID,
            Tracks = [new AiringTrackData(AiringKind.Original, "ja")],
            IsFinished = isFinished,
            TimeZone = timeZone,
            Key = bundle.ChID.ToString(CultureInfo.InvariantCulture),
            Url = SyoboiConstants.GetTitleUrl(bundle.TitleID),
        };
        var schedule = _airingScheduleService.AddOrUpdateSchedule(this, scheduleData);

        var airings = drafts
            .Where(draft => episodesById.ContainsKey(draft.AnidbEpisodeID))
            .Select(draft => new EpisodeAiringData
            {
                Episode = episodesById[draft.AnidbEpisodeID],
                AiredAt = draft.AiredAtUtc,
                OriginalAiredAt = draft.OriginalAiredAtUtc,
                IsDelayed = draft.IsDelayed,
                Key = draft.Key,
            })
            .ToList();
        if (airings.Count is 0)
        {
            _logger.LogDebug("Dropped every airing for AniDB anime {AnimeID} on Syoboi channel {ChannelID}: the episodes they map onto are no longer part of the anime.", anime.ID, bundle.ChID);
            return false;
        }

        // The lookup only asked about a window, so this is a delta: the slots
        // Syoboi still lists are submitted, the ones it has dropped from that
        // window are named as removals, and any airing outside the window is
        // left as it stands.
        //
        // `KeepRemovalsAsHiatus` because a name here means absence, not a
        // deletion: Syoboi dropping a slot from the window it was just asked
        // about is the same signal as leaving it out of a whole-line write, and
        // a future slot that stops being listed is a broadcast pulled rather
        // than one that never existed. The service deletes a named removal
        // otherwise, which is right for a source that states its removals.
        var withdrawn = FindWithdrawnAirings(schedule, airings, fromUtc, toUtc);
        var options = new EpisodeAiringUpdateOptions { KeepRemovalsAsHiatus = true };
        var written = _airingScheduleService.MergeAirings(this, schedule, airings, withdrawn, options);

        // A single broadcast slot covering several episodes (a marathon
        // block, or a double-length episode split into two AniDB entries)
        // shows up as more than one airing sharing the exact same start
        // time; link those together after the write.
        foreach (var slot in written.Where(airing => airing.AiredAt.HasValue).GroupBy(airing => airing.AiredAt!.Value))
        {
            if (slot.Count() > 1)
                _airingScheduleService.LinkAirings(this, slot);
        }

        return true;
    }

    /// <summary>
    /// The airings already on the schedule, inside the window just looked up,
    /// that the lookup no longer lists. A slot Syoboi retracted or moved out
    /// of the window reaches the service through this, the same way leaving
    /// it out of a whole-line write would.
    /// </summary>
    /// <param name="schedule">The schedule being written.</param>
    /// <param name="airings">The airings this write submits.</param>
    /// <param name="fromUtc">The start of the window the lookup asked about, in UTC.</param>
    /// <param name="toUtc">The end of the window the lookup asked about, in UTC.</param>
    /// <returns>The airings to name as removals.</returns>
    private IReadOnlyList<IEpisodeAiring> FindWithdrawnAirings(
        IAiringSchedule schedule,
        IReadOnlyList<EpisodeAiringData> airings,
        DateTime fromUtc,
        DateTime toUtc
    )
    {
        var submitted = new HashSet<string>(airings.Select(airing => airing.Key!), StringComparer.Ordinal);
        return _airingScheduleService
            .GetAiringsForSchedule(schedule.ID, new EpisodeAiringFilteringOptions { IncludeEstimates = false })
            .Where(airing => airing.AiredAt is { } airedAt && airedAt >= fromUtc && airedAt <= toUtc && !submitted.Contains(airing.Key))
            .ToList();
    }

    #endregion

    #region Resolving

    /// <summary>
    /// Resolves the AniDB anime a refresh should be carried out against. A
    /// refresh arrives for whatever entity the caller had in hand, which for
    /// an ordinary series refresh is a shoko series rather than the AniDB
    /// anime Syoboi is keyed through.
    /// </summary>
    /// <param name="series">The series the refresh was requested for.</param>
    /// <returns>The AniDB anime, or <c>null</c> when the series reaches none.</returns>
    private IAnidbAnime? ResolveAnidbAnime(ISeries series)
    {
        if (series is IAnidbAnime anidbAnime)
            return anidbAnime;

        if (series is IShokoSeries shokoSeries &&
            _metadataService.GetSeriesByProviderID(shokoSeries.AnidbAnimeID, IMetadataService.ProviderName.AniDB) is IAnidbAnime resolved)
            return resolved;

        return null;
    }

    #endregion
}
