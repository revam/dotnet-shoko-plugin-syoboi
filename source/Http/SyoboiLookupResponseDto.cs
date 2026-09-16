using System.Collections.Generic;
using Newtonsoft.Json;

namespace Shoko.Plugin.Syoboi.Http;

// Syoboi's db.php JSON API returns every field as a string, including
// numbers, and keys its lookup tables by the entity's own ID (again as a
// string). These DTOs mirror the wire shape exactly; SyoboiResponseParser
// does the string -> typed conversion and drops anything it can't parse.
//
// NOTE: the exact envelope and field names below are transcribed from the
// Syoboi Calendar API documentation described in the airing schedule plan
// rather than verified against a live response from this sandbox (no
// outbound network access here). SyoboiResponseParser is deliberately
// tolerant of missing sections so a small naming mismatch degrades to an
// empty result instead of throwing; see the "Syoboi Calendar" entry in
// this plugin's README for the follow-up to verify this against the real
// API before relying on it in production.

/// <summary>
/// Root shape of a Syoboi <c>db.php</c> lookup response.
/// </summary>
internal sealed class SyoboiLookupResponseDto
{
    [JsonProperty("Programs")]
    public Dictionary<string, SyoboiProgramEntryDto>? Programs { get; set; }

    [JsonProperty("ChLookup")]
    public Dictionary<string, SyoboiChannelDto>? Channels { get; set; }

    [JsonProperty("ChGroupLookup")]
    public Dictionary<string, SyoboiChannelGroupDto>? ChannelGroups { get; set; }
}

/// <summary>
/// Wire shape of one entry in <see cref="SyoboiLookupResponseDto.Programs"/>.
/// </summary>
internal sealed class SyoboiProgramEntryDto
{
    [JsonProperty("PID")]
    public string? PID { get; set; }

    [JsonProperty("TID")]
    public string? TID { get; set; }

    [JsonProperty("StTime")]
    public string? StTime { get; set; }

    [JsonProperty("StOffset")]
    public string? StOffset { get; set; }

    [JsonProperty("EdTime")]
    public string? EdTime { get; set; }

    [JsonProperty("ChID")]
    public string? ChID { get; set; }

    [JsonProperty("Count")]
    public string? Count { get; set; }

    [JsonProperty("Flag")]
    public string? Flag { get; set; }

    [JsonProperty("Deleted")]
    public string? Deleted { get; set; }
}

/// <summary>
/// Wire shape of one entry in <see cref="SyoboiLookupResponseDto.Channels"/>.
/// </summary>
internal sealed class SyoboiChannelDto
{
    [JsonProperty("ChID")]
    public string? ChID { get; set; }

    [JsonProperty("ChName")]
    public string? ChName { get; set; }

    [JsonProperty("ChiEPGName")]
    public string? ChiEPGName { get; set; }

    [JsonProperty("ChGID")]
    public string? ChGID { get; set; }
}

/// <summary>
/// Wire shape of one entry in <see cref="SyoboiLookupResponseDto.ChannelGroups"/>.
/// </summary>
internal sealed class SyoboiChannelGroupDto
{
    [JsonProperty("ChGID")]
    public string? ChGID { get; set; }

    [JsonProperty("ChGName")]
    public string? ChGName { get; set; }
}
