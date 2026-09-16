using System.Collections.Generic;

namespace Shoko.Plugin.Syoboi.Models;

/// <summary>
/// Syoboi's channel list and the channel groups it refers to, as returned by
/// a <c>ChLookup</c> and a <c>ChGroupLookup</c> request. It covers the whole
/// site rather than any one title, changes very rarely, and is a few hundred
/// rows, so it is fetched once and reused across titles.
/// </summary>
/// <param name="Channels">Every channel, keyed by <see cref="SyoboiChannel.ChID"/>.</param>
/// <param name="ChannelGroups">Every channel group, keyed by <see cref="SyoboiChannelGroup.ChGID"/>.</param>
public sealed record SyoboiChannelDirectory(
    IReadOnlyDictionary<int, SyoboiChannel> Channels,
    IReadOnlyDictionary<int, SyoboiChannelGroup> ChannelGroups
)
{
    /// <summary>
    /// An empty directory, returned when the channel list could not be
    /// fetched.
    /// </summary>
    public static SyoboiChannelDirectory Empty { get; } = new(new Dictionary<int, SyoboiChannel>(), new Dictionary<int, SyoboiChannelGroup>());

    /// <summary>
    /// Whether the directory holds anything worth mapping against.
    /// </summary>
    public bool IsEmpty => Channels.Count is 0 || ChannelGroups.Count is 0;
}
