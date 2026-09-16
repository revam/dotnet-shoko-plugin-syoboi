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
/// <param name="ChID">The Syoboi channel ID; also the schedule's key.</param>
/// <param name="ChannelName">The name to register the channel under.</param>
/// <param name="ChannelAliases">Extra names (e.g. the EPG name) to register as aliases.</param>
/// <param name="ChannelType">The channel's classified type.</param>
/// <param name="Programs">The channel's surviving program entries for this title.</param>
public sealed record SyoboiChannelBundle(
    int ChID,
    string ChannelName,
    IReadOnlyList<string> ChannelAliases,
    AiringChannelType ChannelType,
    IReadOnlyList<SyoboiProgramEntry> Programs
);

/// <summary>
/// One program entry mapped onto an AniDB episode, ready to become an
/// episode airing.
/// </summary>
/// <param name="PID">The Syoboi program ID the airing came from.</param>
/// <param name="EpisodeNumber">The AniDB episode number (Syoboi's <c>Count</c>).</param>
/// <param name="AnidbEpisodeID">The AniDB episode ID it was matched to.</param>
/// <param name="AiredAtUtc">The airing's start time in UTC, or <c>null</c> if it couldn't be parsed.</param>
public sealed record SyoboiEpisodeAiringDraft(string PID, int EpisodeNumber, int AnidbEpisodeID, System.DateTime? AiredAtUtc)
{
    /// <summary>
    /// The stable per-schedule key for this airing, as the plan specifies:
    /// <c>{PID}:{Count}</c>.
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
    /// channel group names (<c>ChGName</c>, matched case-insensitively) are
    /// included. <c>null</c> or empty means every non-radio group.
    /// </param>
    /// <returns>One bundle per channel that has at least one usable program entry.</returns>
    public static IReadOnlyList<SyoboiChannelBundle> BuildChannelBundles(
        int titleId,
        SyoboiLookupResult lookup,
        IReadOnlySet<string>? allowedChannelGroupNames = null)
    {
        var bundles = new List<SyoboiChannelBundle>();
        var programsByChannel = lookup.Programs
            .Where(entry => entry.TID == titleId && !SyoboiProgramFilter.ShouldSkip(entry))
            .GroupBy(entry => entry.ChID);

        foreach (var group in programsByChannel)
        {
            if (!lookup.Channels.TryGetValue(group.Key, out var channel))
                continue;

            if (!lookup.ChannelGroups.TryGetValue(channel.ChGID, out var channelGroup))
                continue;

            if (allowedChannelGroupNames is { Count: > 0 } && !allowedChannelGroupNames.Contains(channelGroup.ChGName))
                continue;

            var channelType = SyoboiChannelGroupClassifier.Classify(channelGroup.ChGName);
            if (channelType is null)
                continue; // Radio.

            var displayName = SyoboiChannelNaming.ResolveDisplayName(channel.ChName, channelType.Value);
            IReadOnlyList<string> aliases = !string.IsNullOrWhiteSpace(channel.ChiEPGName) && channel.ChiEPGName != displayName
                ? [channel.ChiEPGName]
                : [];

            bundles.Add(new SyoboiChannelBundle(channel.ChID, displayName, aliases, channelType.Value, [.. group]));
        }

        return bundles;
    }

    /// <summary>
    /// Maps a channel bundle's program entries onto AniDB episode IDs,
    /// dropping entries whose episode number has no matching normal episode.
    /// </summary>
    /// <param name="programs">The program entries to map.</param>
    /// <param name="anidbEpisodeIdsByNumber">
    /// The anime's normal-episode AniDB episode IDs, keyed by episode number.
    /// </param>
    /// <returns>One draft per program entry that maps to a known episode.</returns>
    public static IReadOnlyList<SyoboiEpisodeAiringDraft> BuildEpisodeDrafts(
        IReadOnlyList<SyoboiProgramEntry> programs,
        IReadOnlyDictionary<int, int> anidbEpisodeIdsByNumber)
    {
        var drafts = new List<SyoboiEpisodeAiringDraft>();
        foreach (var entry in programs)
        {
            if (entry.Count is not { } episodeNumber)
                continue;

            if (!anidbEpisodeIdsByNumber.TryGetValue(episodeNumber, out var episodeId))
                continue;

            var airedAt = SyoboiTimeConverter.ToUtc(entry.StTime, entry.StOffset);
            drafts.Add(new SyoboiEpisodeAiringDraft(entry.PID, episodeNumber, episodeId, airedAt));
        }

        return drafts;
    }
}
