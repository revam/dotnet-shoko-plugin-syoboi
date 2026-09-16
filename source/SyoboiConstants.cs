using System;

namespace Shoko.Plugin.Syoboi;

/// <summary>
/// Constants for talking to cal.syoboi.jp.
/// </summary>
public static class SyoboiConstants
{
    /// <summary>
    /// Base URL for the Syoboi Calendar JSON API.
    /// </summary>
    public const string BaseUrl = "https://cal.syoboi.jp/";

    /// <summary>
    /// Relative path of the lookup endpoint used for every request this
    /// plugin makes (<c>Command=ProgLookup</c>, <c>ChLookup</c> and
    /// <c>ChGroupLookup</c> all come back together in one response).
    /// </summary>
    public const string LookupPath = "db.php";

    /// <summary>
    /// The minimum interval between requests. cal.syoboi.jp allows one
    /// request per second for clients that send a custom User-Agent;
    /// clients that don't are slowed to one per ten seconds.
    /// </summary>
    public static readonly TimeSpan MinRequestInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The IANA time zone id Syoboi's own times (<c>StTime</c>/<c>EdTime</c>)
    /// are given in. Japan has no daylight saving time, so this is a fixed
    /// +09:00 offset year-round.
    /// </summary>
    public const string TimeZoneId = "Asia/Tokyo";
}
