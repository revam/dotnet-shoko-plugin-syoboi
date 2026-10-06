using System;
using System.Collections.Generic;
using System.Linq;
using Shoko.Abstractions.Metadata.Airing;
using Shoko.Plugin.Syoboi.Models;

namespace Shoko.Plugin.Syoboi.Mapping;

/// <summary>
/// One channel's worth of a title's programs, ready to become a schedule.
/// One of these is produced per (title, channel) pair that survives
/// filtering, exactly matching "one schedule per AniDB anime and ChID" from
/// the airing schedule plan.
/// </summary>
/// <param name="TitleID">The Syoboi title ID the programs belong to.</param>
/// <param name="ChID">The Syoboi channel ID; also the schedule's key.</param>
/// <param name="ChannelName">The name to register the channel under.</param>
/// <param name="ChannelAliases">Extra names (e.g. the EPG name) to register as aliases.</param>
/// <param name="ChannelType">The channel's classified type.</param>
/// <param name="ChannelCountryCode">The country to register the channel under, or <c>null</c> for a global service.</param>
/// <param name="Programs">The channel's surviving program entries for this title.</param>
public sealed record SyoboiChannelBundle(
    int TitleID,
    int ChID,
    string ChannelName,
    IReadOnlyList<string> ChannelAliases,
    AiringChannelType ChannelType,
    string? ChannelCountryCode,
    IReadOnlyList<SyoboiProgramEntry> Programs
);

/// <summary>
/// One program entry placed on the schedule's numbered line, ready to become
/// an episode airing.
/// </summary>
/// <param name="PID">The Syoboi program ID the airing came from.</param>
/// <param name="EpisodeNumber">Syoboi's own episode number (its <c>Count</c>).</param>
/// <param name="SequenceNumber">
/// The airing's place on the line: <paramref name="EpisodeNumber"/> less the
/// title's count offset, so the AniDB episode number it stands for.
/// </param>
/// <param name="AiredAtUtc">The airing's start time in UTC, or <c>null</c> if it couldn't be parsed.</param>
/// <param name="OriginalAiredAtUtc">
/// The slot the run normally occupies, in UTC, when this one was pushed back
/// from it; otherwise <c>null</c>.
/// </param>
/// <param name="IsDelayed">Whether this slot was pushed back from the run's usual time.</param>
/// <param name="Kind">
/// Whether the slot is one of the regular run, an advance airing or a rerun.
/// </param>
public sealed record SyoboiEpisodeAiringDraft(
    string PID,
    int EpisodeNumber,
    int SequenceNumber,
    DateTime? AiredAtUtc,
    DateTime? OriginalAiredAtUtc = null,
    bool IsDelayed = false,
    EpisodeAiringKind Kind = EpisodeAiringKind.Normal
)
{
    /// <summary>
    /// The stable per-schedule key for this airing, as the plan specifies:
    /// <c>{PID}:{Count}</c>. Every slot has a PID of its own, so an advance
    /// airing or a rerun never shares a key with the episode's regular
    /// showing on the same channel.
    /// </summary>
    public string Key => $"{PID}:{EpisodeNumber}";
}

/// <summary>
/// Pure mapping logic turning a parsed Syoboi lookup result into the shape
/// <see cref="Shoko.Plugin.Syoboi.SyoboiAiringScheduleProvider"/> writes
/// through <c>IAiringScheduleService</c>. Kept free of any abstractions
/// entity type (<c>IAnidbAnime</c>, <c>IAnidbEpisode</c>, …) so it can be
/// tested with plain records instead of mocking the metadata model.
/// </summary>
public static class SyoboiScheduleMapper
{
    /// <summary>
    /// Groups one title's surviving program entries by channel, classifying
    /// and naming each channel, and dropping radio and disallowed groups.
    /// </summary>
    /// <param name="titleId">The Syoboi title ID to build bundles for.</param>
    /// <param name="lookup">The lookup result the entries, channels and channel groups come from.</param>
    /// <param name="allowedChannelGroupNames">
    /// Optional. When non-empty, only channels belonging to one of these
    /// channel group names (<c>ChGroupName</c>, matched case-insensitively) are
    /// included. <c>null</c> or empty means every non-radio group.
    /// </param>
    /// <param name="allowMissingEpisodeNumbers">
    /// Whether slots with no <c>Count</c> are kept; see
    /// <see cref="SyoboiProgramFilter.ShouldSkip(SyoboiProgramEntry, bool)"/>.
    /// </param>
    /// <returns>One bundle per channel that has at least one usable program entry.</returns>
    public static IReadOnlyList<SyoboiChannelBundle> BuildChannelBundles(
        int titleId,
        SyoboiLookupResult lookup,
        IReadOnlySet<string>? allowedChannelGroupNames = null,
        bool allowMissingEpisodeNumbers = false)
    {
        var bundles = new List<SyoboiChannelBundle>();
        var programsByChannel = lookup.Programs
            .Where(entry => entry.TID == titleId && !SyoboiProgramFilter.ShouldSkip(entry, allowMissingEpisodeNumbers))
            .GroupBy(entry => entry.ChID);

        foreach (var group in programsByChannel)
        {
            if (!lookup.Channels.TryGetValue(group.Key, out var channel))
                continue;

            if (!lookup.ChannelGroups.TryGetValue(channel.ChGID, out var channelGroup))
                continue;

            if (allowedChannelGroupNames is { Count: > 0 } && !allowedChannelGroupNames.Contains(channelGroup.ChGroupName))
                continue;

            var channelType = SyoboiChannelGroupClassifier.Classify(channelGroup.ChGroupName);
            if (channelType is null)
                continue; // Radio.

            var displayName = SyoboiChannelNaming.ResolveDisplayName(channel.ChName);
            var countryCode = SyoboiChannelNaming.ResolveCountryCode(channel.ChName, channelType.Value);
            IReadOnlyList<string> aliases = !string.IsNullOrWhiteSpace(channel.ChiEPGName) && channel.ChiEPGName != displayName
                ? [channel.ChiEPGName]
                : [];

            bundles.Add(new SyoboiChannelBundle(titleId, channel.ChID, displayName, aliases, channelType.Value, countryCode, [.. group]));
        }

        return bundles;
    }

    /// <summary>
    /// Places a channel bundle's program entries on the schedule's numbered
    /// line, whether or not AniDB lists those episodes yet. A slot covering an
    /// episode range becomes one draft per episode, all at the slot's time.
    /// </summary>
    /// <param name="programs">The program entries to map.</param>
    /// <param name="isSingleEpisode">
    /// Whether the anime is a single episode, such as a film, so a slot with no
    /// episode number of its own is sequence <c>1</c>.
    /// </param>
    /// <param name="countOffset">
    /// How far Syoboi's count runs ahead of AniDB's numbering; see
    /// <see cref="FindCountOffset"/>.
    /// </param>
    /// <returns>One draft per episode of each program entry with a place of <c>1</c> or more.</returns>
    public static IReadOnlyList<SyoboiEpisodeAiringDraft> BuildEpisodeDrafts(
        IReadOnlyList<SyoboiProgramEntry> programs,
        bool isSingleEpisode = false,
        int countOffset = 0)
    {
        var drafts = new List<SyoboiEpisodeAiringDraft>();
        foreach (var entry in programs)
        {
            foreach (var (episodeNumber, sequenceNumber) in GetPlaces(entry, isSingleEpisode, countOffset))
            {
                if (sequenceNumber < 1)
                    continue;

                drafts.Add(new SyoboiEpisodeAiringDraft(entry.PID, episodeNumber, sequenceNumber, entry.StartedAt, entry.OriginalStartedAt, entry.IsDelayed, entry.Kind));
            }
        }

        return drafts;
    }

    /// <summary>
    /// Works out how far a title's <c>Count</c> runs ahead of AniDB's episode
    /// numbers, for a title that keeps counting across cours AniDB splits
    /// into separate anime. A regular slot of count <c>c</c> on the air date
    /// of AniDB episode <c>n</c> votes for <c>c - n</c>, and the most votes
    /// win, the offset nearest <c>0</c> on a tie.
    /// </summary>
    /// <param name="programs">The title's program entries, on any channel.</param>
    /// <param name="episodeNumbersByAirDate">The anime's normal episode numbers, keyed by their air date.</param>
    /// <returns>The offset to subtract from a count; <c>0</c> when no slot falls on a known air date.</returns>
    public static int FindCountOffset(
        IEnumerable<SyoboiProgramEntry> programs,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<int>> episodeNumbersByAirDate)
    {
        var votes = new Dictionary<int, int>();
        foreach (var entry in programs)
        {
            if (entry.Deleted || entry.Kind is not EpisodeAiringKind.Normal || entry.Count is not > 0 || entry.StartedAt is not { } startedAt)
                continue;

            foreach (var date in GetAirDates(startedAt))
            {
                if (!episodeNumbersByAirDate.TryGetValue(date, out var episodeNumbers))
                    continue;

                foreach (var episodeNumber in episodeNumbers)
                    votes[entry.Count.Value - episodeNumber] = votes.GetValueOrDefault(entry.Count.Value - episodeNumber) + 1;
            }
        }

        return votes.Count is 0
            ? 0
            : votes
                .OrderByDescending(vote => vote.Value)
                .ThenBy(vote => Math.Abs(vote.Key))
                .ThenBy(vote => vote.Key)
                .First().Key;
    }

    /// <summary>
    /// The days a slot may be listed under: its date in Japan, and the day
    /// before for a late-night slot, which Japanese listings write as hour
    /// 24 to 28 of the previous day.
    /// </summary>
    /// <param name="startedAt">The slot's start, in UTC.</param>
    /// <returns>The dates.</returns>
    private static IEnumerable<DateOnly> GetAirDates(DateTime startedAt)
    {
        var japanLocal = startedAt + SyoboiConstants.TimeZoneOffset;
        var date = DateOnly.FromDateTime(japanLocal);
        yield return date;

        if (japanLocal.Hour < 5)
            yield return date.AddDays(-1);
    }

    /// <summary>
    /// The places a program entry takes on the line, with the Syoboi episode
    /// number each one is keyed by.
    /// </summary>
    /// <param name="entry">The program entry.</param>
    /// <param name="isSingleEpisode">Whether the anime is a single episode.</param>
    /// <param name="countOffset">How far Syoboi's count runs ahead of AniDB's numbering.</param>
    /// <returns>The episode and sequence numbers, in order; empty when the entry names none.</returns>
    private static IEnumerable<(int EpisodeNumber, int SequenceNumber)> GetPlaces(SyoboiProgramEntry entry, bool isSingleEpisode, int countOffset)
    {
        // A film or a one-off special has a single broadcast and no episode
        // number to go with it, so it can only be the first place.
        if (entry.Count is null && entry.EpisodeRange is null)
            return isSingleEpisode ? [(1, 1)] : [];

        return GetEpisodeNumbers(entry).Select(episodeNumber => (episodeNumber, episodeNumber - countOffset));
    }

    /// <summary>
    /// The episode numbers a program entry is for: its <c>Count</c>, else the
    /// range in its subtitle.
    /// </summary>
    /// <param name="entry">The program entry.</param>
    /// <returns>The episode numbers, in order; empty when the entry names none.</returns>
    private static IEnumerable<int> GetEpisodeNumbers(SyoboiProgramEntry entry)
    {
        if (entry.Count is { } count)
            return [count];

        if (entry.EpisodeRange is (var first, var last))
            return Enumerable.Range(first, last - first + 1);

        return [];
    }
}
