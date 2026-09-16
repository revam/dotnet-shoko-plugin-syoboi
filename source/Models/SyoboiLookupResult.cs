using System.Collections.Generic;

namespace Shoko.Plugin.Syoboi.Models;

/// <summary>
/// The parsed contents of a Syoboi lookup response: the program entries for
/// every title asked for, plus the channel and channel group lookups needed
/// to make sense of them. One request can (and normally does) cover many
/// title IDs at once.
/// </summary>
/// <param name="Programs">Every program entry returned, across all requested titles.</param>
/// <param name="Channels">Every channel Syoboi described, keyed by <see cref="SyoboiChannel.ChID"/>.</param>
/// <param name="ChannelGroups">Every channel group Syoboi described, keyed by <see cref="SyoboiChannelGroup.ChGID"/>.</param>
public sealed record SyoboiLookupResult(
    IReadOnlyList<SyoboiProgramEntry> Programs,
    IReadOnlyDictionary<int, SyoboiChannel> Channels,
    IReadOnlyDictionary<int, SyoboiChannelGroup> ChannelGroups
)
{
    /// <summary>
    /// An empty result, returned when a request fails or a response cannot
    /// be parsed.
    /// </summary>
    public static SyoboiLookupResult Empty { get; } = new([], new Dictionary<int, SyoboiChannel>(), new Dictionary<int, SyoboiChannelGroup>());
}
