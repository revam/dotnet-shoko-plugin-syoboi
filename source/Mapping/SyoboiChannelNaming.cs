using System;
using System.Collections.Generic;
using Shoko.Abstractions.Metadata.Airing;

namespace Shoko.Plugin.Syoboi.Mapping;

/// <summary>
/// Resolves the name and country to register a Syoboi channel under. Every
/// channel keeps the name Syoboi gives it. Syoboi lists Japanese channels, so
/// a TV station, and a streaming service only available in Japan, is
/// registered in Japan, while a global streaming service is registered
/// without a country, so it is the same channel whoever reports it.
/// </summary>
public static class SyoboiChannelNaming
{
    /// <summary>
    /// The country Syoboi's channels are in.
    /// </summary>
    public const string RegionCountryCode = "JP";

    // Global brands Syoboi lists under their worldwide name. Hulu is not one
    // of them: Syoboi's Hulu is Hulu Japan, a service of its own.
    private static readonly HashSet<string> GlobalStreamingBrands = new(StringComparer.OrdinalIgnoreCase)
    {
        "Amazon",
        "Amazon Prime Video",
        "Crunchyroll",
        "Disney+",
        "Disney Plus",
        "Netflix",
        "YouTube",
    };

    /// <summary>
    /// Resolves the name to register a channel under.
    /// </summary>
    /// <param name="channelName">The channel's Syoboi <c>ChName</c>.</param>
    /// <returns>
    /// The name to pass to <c>IAiringScheduleService.FindOrRegisterChannel</c>.
    /// </returns>
    public static string ResolveDisplayName(string channelName)
    {
        ArgumentNullException.ThrowIfNull(channelName);

        return channelName.Trim();
    }

    /// <summary>
    /// Resolves the country to register a channel under.
    /// </summary>
    /// <param name="channelName">The channel's Syoboi <c>ChName</c>.</param>
    /// <param name="channelType">The channel's classified type.</param>
    /// <returns>
    /// <see cref="RegionCountryCode"/>, or <c>null</c> for a global streaming
    /// service.
    /// </returns>
    public static string? ResolveCountryCode(string channelName, AiringChannelType channelType)
    {
        ArgumentNullException.ThrowIfNull(channelName);

        return channelType == AiringChannelType.Streaming && GlobalStreamingBrands.Contains(channelName.Trim())
            ? null
            : RegionCountryCode;
    }
}
