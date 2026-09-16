using System;
using System.Collections.Generic;
using Shoko.Abstractions.Metadata.Airing;
using Shoko.Abstractions.Metadata.Services;

namespace Shoko.Plugin.Syoboi.Mapping;

/// <summary>
/// Resolves the display name to register a Syoboi channel under. Most
/// channels are registered exactly as Syoboi names them; a handful of
/// international streaming brands are registered as Japanese regional
/// channels instead, so they share an ID with the same brand as reported by
/// another provider rather than colliding under a bare, ambiguous name.
/// </summary>
public static class SyoboiChannelNaming
{
    /// <summary>
    /// The country Syoboi's streaming channels are watched from.
    /// </summary>
    public const string RegionCountryCode = "JP";

    // International brands Syoboi lists under their bare, worldwide name.
    // Anything else (ABEMA, dアニメストア, and every TV station) is already
    // unambiguous and is registered as Syoboi names it.
    private static readonly HashSet<string> InternationalStreamingBrands = new(StringComparer.OrdinalIgnoreCase)
    {
        "Netflix",
        "Amazon",
        "Amazon Prime Video",
        "Disney+",
        "Disney Plus",
        "Hulu",
    };

    /// <summary>
    /// Resolves the name to register a channel under.
    /// </summary>
    /// <param name="channelName">The channel's Syoboi <c>ChName</c>.</param>
    /// <param name="channelType">The channel's classified type.</param>
    /// <returns>
    /// The name to pass to <c>IAiringScheduleService.FindOrRegisterChannel</c>.
    /// </returns>
    public static string ResolveDisplayName(string channelName, AiringChannelType channelType)
    {
        ArgumentNullException.ThrowIfNull(channelName);

        var trimmed = channelName.Trim();
        if (channelType == AiringChannelType.Streaming && InternationalStreamingBrands.Contains(trimmed))
            return IAiringScheduleService.GetRegionalChannelName(trimmed, RegionCountryCode);

        return trimmed;
    }
}
