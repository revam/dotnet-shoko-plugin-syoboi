using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Shoko.Plugin.Syoboi.Models;

namespace Shoko.Plugin.Syoboi.Http;

/// <summary>
/// Parses a Syoboi <c>db.php</c> JSON response into typed, ready-to-use
/// records. Kept free of any I/O so it can be unit tested with plain JSON
/// strings.
/// </summary>
public static class SyoboiResponseParser
{
    /// <summary>
    /// Parses a Syoboi lookup response. Never throws: a malformed document,
    /// or one missing a section entirely, comes back as an empty (or
    /// partially empty) result instead, since a client-side parsing gap
    /// should not take down the sweep for every other title in the request.
    /// </summary>
    /// <param name="json">The raw JSON response body.</param>
    /// <param name="logger">Optional. Used to log entries that can't be parsed.</param>
    /// <returns>The parsed result.</returns>
    public static SyoboiLookupResult Parse(string json, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            return SyoboiLookupResult.Empty;

        SyoboiLookupResponseDto? dto;
        try
        {
            dto = JsonConvert.DeserializeObject<SyoboiLookupResponseDto>(json);
        }
        catch (JsonException ex)
        {
            logger?.LogWarning(ex, "Failed to parse Syoboi Calendar response as JSON.");
            return SyoboiLookupResult.Empty;
        }

        if (dto is null)
            return SyoboiLookupResult.Empty;

        var programs = new List<SyoboiProgramEntry>();
        if (dto.Programs is not null)
        {
            foreach (var (key, entry) in dto.Programs)
            {
                if (TryMapProgram(key, entry, logger) is { } mapped)
                    programs.Add(mapped);
            }
        }

        var channels = new Dictionary<int, SyoboiChannel>();
        if (dto.Channels is not null)
        {
            foreach (var (key, entry) in dto.Channels)
            {
                if (TryMapChannel(key, entry, logger) is { } mapped)
                    channels[mapped.ChID] = mapped;
            }
        }

        var channelGroups = new Dictionary<int, SyoboiChannelGroup>();
        if (dto.ChannelGroups is not null)
        {
            foreach (var (key, entry) in dto.ChannelGroups)
            {
                if (TryMapChannelGroup(key, entry, logger) is { } mapped)
                    channelGroups[mapped.ChGID] = mapped;
            }
        }

        return new SyoboiLookupResult(programs, channels, channelGroups);
    }

    private static SyoboiProgramEntry? TryMapProgram(string key, SyoboiProgramEntryDto dto, ILogger? logger)
    {
        var pid = FirstNonEmpty(dto.PID, key);
        if (!TryParseInt(dto.TID, out var tid) || !TryParseInt(dto.ChID, out var chId))
        {
            logger?.LogDebug("Skipping Syoboi program {PID}: missing or invalid TID/ChID.", pid);
            return null;
        }

        var count = TryParseInt(dto.Count, out var countValue) && countValue > 0 ? countValue : (int?)null;
        TryParseInt(dto.StOffset, out var offset);
        TryParseInt(dto.Flag, out var flag);
        var deleted = dto.Deleted is "1";

        return new SyoboiProgramEntry(pid, tid, chId, dto.StTime, offset, dto.EdTime, count, flag, deleted);
    }

    private static SyoboiChannel? TryMapChannel(string key, SyoboiChannelDto dto, ILogger? logger)
    {
        if (!TryParseInt(FirstNonEmpty(dto.ChID, key), out var chId))
        {
            logger?.LogDebug("Skipping Syoboi channel {Key}: missing or invalid ChID.", key);
            return null;
        }

        if (string.IsNullOrWhiteSpace(dto.ChName))
        {
            logger?.LogDebug("Skipping Syoboi channel {ChID}: missing ChName.", chId);
            return null;
        }

        TryParseInt(dto.ChGID, out var chGid);
        var epgName = string.IsNullOrWhiteSpace(dto.ChiEPGName) ? null : dto.ChiEPGName;
        return new SyoboiChannel(chId, dto.ChName, epgName, chGid);
    }

    private static SyoboiChannelGroup? TryMapChannelGroup(string key, SyoboiChannelGroupDto dto, ILogger? logger)
    {
        if (!TryParseInt(FirstNonEmpty(dto.ChGID, key), out var chGid))
        {
            logger?.LogDebug("Skipping Syoboi channel group {Key}: missing or invalid ChGID.", key);
            return null;
        }

        if (string.IsNullOrWhiteSpace(dto.ChGName))
        {
            logger?.LogDebug("Skipping Syoboi channel group {ChGID}: missing ChGName.", chGid);
            return null;
        }

        return new SyoboiChannelGroup(chGid, dto.ChGName);
    }

    private static string FirstNonEmpty(string? preferred, string fallback)
        => string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;

    private static bool TryParseInt(string? value, out int result)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
}
