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
/// Only <see cref="AiringKind.Original"/> is ever produced: Syoboi tracks the
/// original Japanese broadcast/stream, not subtitled or dubbed releases.
/// </remarks>
public sealed class SyoboiAiringScheduleProvider : IAiringScheduleProvider<Configuration>
{
    private static readonly IReadOnlySet<AiringKind> _availableKinds = new HashSet<AiringKind> { AiringKind.Original };

    private readonly ILogger<SyoboiAiringScheduleProvider> _logger;
    private readonly ConfigurationProvider<Configuration> _configurationProvider;
    private readonly IAiringScheduleService _airingScheduleService;
    private readonly IMetadataService _metadataService;
    private readonly SyoboiApiClient _apiClient;

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
    public SyoboiAiringScheduleProvider(
        ILogger<SyoboiAiringScheduleProvider> logger,
        ConfigurationProvider<Configuration> configurationProvider,
        IAiringScheduleService airingScheduleService,
        IMetadataService metadataService,
        SyoboiApiClient apiClient)
    {
        _logger = logger;
        _configurationProvider = configurationProvider;
        _airingScheduleService = airingScheduleService;
        _metadataService = metadataService;
        _apiClient = apiClient;
    }

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

        var lookup = await _apiClient.ProgLookupAsync([titleId], cancellationToken: cancellationToken).ConfigureAwait(false);
        return ApplyLookupResult(anime, titleId, lookup);
    }

    /// <summary>
    /// Applies an already-fetched lookup result to one AniDB anime, writing
    /// channels, schedules and airings through <c>IAiringScheduleService</c>.
    /// Shared between <see cref="RefreshAsync(ISeries,CancellationToken)"/>
    /// (a single-title request) and the recurring sweep job (one request
    /// covering every tracked title at once).
    /// </summary>
    /// <param name="anime">The AniDB anime the lookup is for.</param>
    /// <param name="titleId">The Syoboi title ID <paramref name="anime"/> was looked up by.</param>
    /// <param name="lookup">The lookup result, which may cover other titles too.</param>
    /// <returns><c>true</c> if at least one schedule was written.</returns>
    public bool ApplyLookupResult(IAnidbAnime anime, int titleId, SyoboiLookupResult lookup)
    {
        ArgumentNullException.ThrowIfNull(anime);
        ArgumentNullException.ThrowIfNull(lookup);

        var config = _configurationProvider.Load();
        var allowedGroups = config.AllowedChannelGroups is { Count: > 0 }
            ? new HashSet<string>(config.AllowedChannelGroups, StringComparer.OrdinalIgnoreCase)
            : null;

        var bundles = SyoboiScheduleMapper.BuildChannelBundles(titleId, lookup, allowedGroups);
        if (bundles.Count == 0)
            return false;

        var episodesByNumber = anime.Episodes
            .Where(episode => episode.Type == EpisodeType.Episode)
            .GroupBy(episode => episode.EpisodeNumber)
            .ToDictionary(group => group.Key, group => group.First().ID);
        var episodesById = anime.Episodes.ToDictionary(episode => episode.ID);

        var timeZone = TimeZoneInfo.TryFindSystemTimeZoneById(SyoboiConstants.TimeZoneId, out var tz) ? tz : null;
        var isFinished = anime.EndDate.HasValue;

        var wroteAny = false;
        foreach (var bundle in bundles)
        {
            if (!TryWriteSchedule(anime, bundle, timeZone, isFinished, episodesByNumber, episodesById))
                continue;

            wroteAny = true;
        }

        return wroteAny;
    }

    private bool TryWriteSchedule(
        IAnidbAnime anime,
        SyoboiChannelBundle bundle,
        TimeZoneInfo? timeZone,
        bool isFinished,
        IReadOnlyDictionary<int, int> episodesByNumber,
        IReadOnlyDictionary<int, IAnidbEpisode> episodesById)
    {
        var drafts = SyoboiScheduleMapper.BuildEpisodeDrafts(bundle.Programs, episodesByNumber);
        if (drafts.Count == 0)
            return false;

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
        };
        var schedule = _airingScheduleService.AddOrUpdateSchedule(this, scheduleData);

        var airings = drafts
            .Where(draft => episodesById.ContainsKey(draft.AnidbEpisodeID))
            .Select(draft => new EpisodeAiringData
            {
                Episode = episodesById[draft.AnidbEpisodeID],
                AiredAt = draft.AiredAtUtc,
                Key = draft.Key,
            })
            .ToList();
        if (airings.Count == 0)
            return false;

        var written = _airingScheduleService.SetAirings(this, schedule, airings);

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

    private IAnidbAnime? ResolveAnidbAnime(ISeries series)
    {
        if (series is IAnidbAnime anidbAnime)
            return anidbAnime;

        if (series is IShokoSeries shokoSeries &&
            _metadataService.GetSeriesByProviderID(shokoSeries.AnidbAnimeID, IMetadataService.ProviderName.AniDB) is IAnidbAnime resolved)
            return resolved;

        return null;
    }
}
