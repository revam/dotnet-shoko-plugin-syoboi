using System.Collections.Generic;

namespace Shoko.Plugin.Syoboi.Models;

/// <summary>
/// The program entries for every title a sweep asked about, together with the
/// channel directory needed to make sense of them. Syoboi answers one command
/// per request, so this is assembled from a <c>ProgLookup</c> request per
/// batch of titles plus the (cached) <c>ChLookup</c>/<c>ChGroupLookup</c>
/// pair.
/// </summary>
/// <param name="Programs">Every program entry returned, across all requested titles.</param>
/// <param name="Directory">The channel directory the entries' <c>ChID</c>s refer to.</param>
public sealed record SyoboiLookupResult(
    IReadOnlyList<SyoboiProgramEntry> Programs,
    SyoboiChannelDirectory Directory
)
{
    /// <summary>
    /// Every channel Syoboi knows of, keyed by <see cref="SyoboiChannel.ChID"/>.
    /// </summary>
    public IReadOnlyDictionary<int, SyoboiChannel> Channels => Directory.Channels;

    /// <summary>
    /// Every channel group Syoboi knows of, keyed by <see cref="SyoboiChannelGroup.ChGID"/>.
    /// </summary>
    public IReadOnlyDictionary<int, SyoboiChannelGroup> ChannelGroups => Directory.ChannelGroups;

    /// <summary>
    /// An empty result, returned when a request fails or a response cannot
    /// be parsed.
    /// </summary>
    public static SyoboiLookupResult Empty { get; } = new([], SyoboiChannelDirectory.Empty);
}
