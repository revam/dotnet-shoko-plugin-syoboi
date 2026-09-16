using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Shoko.Abstractions.Config;
using Shoko.Abstractions.Metadata.Airing;

namespace Shoko.Plugin.Syoboi;

/// <summary>
/// Configures the Syoboi Calendar airing schedule provider.
/// </summary>
/// <remarks>
/// cal.syoboi.jp is a small, community-run site. The settings here exist to
/// be a good citizen of it: only sweep titles that are actually relevant,
/// and let an operator narrow which channel groups get tracked at all.
/// </remarks>
[Display(Name = "Syoboi Calendar")]
public class Configuration : IAiringScheduleProviderConfiguration, INewtonsoftJsonConfiguration
{
    /// <summary>
    /// Only sweep AniDB anime that are currently airing, about to air, or
    /// recently ended, rather than every anime with a Syoboi title ID ever
    /// seen locally.
    /// </summary>
    [Display(Name = "Only Sweep Currently Relevant Anime")]
    [DefaultValue(true)]
    public bool ActiveOnly { get; set; } = true;

    /// <summary>
    /// How many days before an anime's known air date it starts being swept,
    /// when <see cref="ActiveOnly"/> is on.
    /// </summary>
    [Display(Name = "Upcoming Window (Days)")]
    [Range(0, 3650)]
    [DefaultValue(30)]
    public int UpcomingWindowDays { get; set; } = 30;

    /// <summary>
    /// How many days after an anime's end date it keeps being swept, when
    /// <see cref="ActiveOnly"/> is on. Covers a run whose last broadcast slot
    /// airs after the nominal end date, and any last-minute reschedules.
    /// </summary>
    [Display(Name = "Recently Ended Window (Days)")]
    [Range(0, 3650)]
    [DefaultValue(90)]
    public int RecentlyEndedWindowDays { get; set; } = 90;

    /// <summary>
    /// How often the sweep job re-fetches every tracked title in one request.
    /// Syoboi asks clients to be gentle; there is little reason to sweep more
    /// often than weekly.
    /// </summary>
    [Display(Name = "Sweep Interval (Hours)")]
    [Range(1, 24 * 30)]
    [DefaultValue(24 * 7)]
    public int SweepIntervalHours { get; set; } = 24 * 7;

    /// <summary>
    /// Restricts which Syoboi channel groups (<c>ChGroupLookup</c>'s
    /// <c>ChGroupName</c>) are tracked, e.g. <c>テレビ 関東</c>,
    /// <c>BSデジタル</c>, <c>スカパー</c>, <c>インターネット</c> or
    /// <c>AbemaTV</c>. Empty means every non-radio group. Radio is always
    /// skipped regardless of this list.
    /// </summary>
    [Display(Name = "Allowed Channel Groups")]
    public List<string> AllowedChannelGroups { get; set; } = [];

    /// <summary>
    /// The application name sent as the product token of the User-Agent
    /// header, as <c>"AppName (+Url)"</c>. Syoboi throttles clients that omit
    /// a custom User-Agent much harder than the normal 1 request/second.
    /// </summary>
    [Display(Name = "User-Agent App Name")]
    [DefaultValue("Shoko.Plugin.Syoboi")]
    public string UserAgentAppName { get; set; } = "Shoko.Plugin.Syoboi";

    /// <summary>
    /// The contact URL sent as the comment part of the User-Agent header.
    /// </summary>
    [Display(Name = "User-Agent Contact URL")]
    [DefaultValue("https://github.com/ShokoAnime/ShokoServer")]
    public string UserAgentUrl { get; set; } = "https://github.com/ShokoAnime/ShokoServer";
}
