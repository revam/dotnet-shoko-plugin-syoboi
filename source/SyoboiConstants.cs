using System;
using System.Globalization;

namespace Shoko.Plugin.Syoboi;

/// <summary>
/// Constants for talking to cal.syoboi.jp.
/// </summary>
/// <remarks>
/// Everything here is verified against the live service; see
/// <see href="https://docs.cal.syoboi.jp/spec/db.php/"/> for the endpoint
/// itself and <see href="https://docs.cal.syoboi.jp/spec/rate_limit/"/> for
/// the rate limits.
/// </remarks>
public static class SyoboiConstants
{
    /// <summary>
    /// Base URL for the Syoboi Calendar API.
    /// </summary>
    public const string BaseUrl = "https://cal.syoboi.jp/";

    /// <summary>
    /// Relative path of the database endpoint every request this plugin makes
    /// goes to. It always answers in XML — the <c>json.php</c> endpoint only
    /// serves title and channel metadata, never broadcast slots, so it is of
    /// no use here.
    /// </summary>
    /// <remarks>
    /// One request carries exactly one <c>Command</c>: <c>ProgLookup</c>,
    /// <c>ChLookup</c>, <c>ChGroupLookup</c> and <c>TitleLookup</c> are each a
    /// request of their own.
    /// </remarks>
    public const string DatabasePath = "db.php";

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

    /// <summary>
    /// The fixed offset of <see cref="TimeZoneId"/> from UTC.
    /// </summary>
    public static readonly TimeSpan TimeZoneOffset = TimeSpan.FromHours(9);

    /// <summary>
    /// How <c>StTime</c> and <c>EdTime</c> are formatted in a response: local
    /// Japanese time, with no zone marker.
    /// </summary>
    public const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>
    /// How the bounds of a <c>Range</c> request parameter are formatted. Also
    /// Japanese local time, but in a different shape to the one responses use.
    /// </summary>
    public const string RangeBoundFormat = "yyyyMMdd_HHmmss";

    /// <summary>
    /// The most <c>ProgItem</c>s a single <c>ProgLookup</c> request answers
    /// with, however many titles it asks about or how wide a range it covers.
    /// </summary>
    public const int MaxProgramsPerRequest = 5000;

    /// <summary>
    /// Formats the URL of a title's broadcast slot page, used as an airing
    /// schedule's <c>Url</c>.
    /// </summary>
    /// <param name="titleId">The Syoboi title ID.</param>
    /// <returns>The title's page on cal.syoboi.jp.</returns>
    public static string GetTitleUrl(int titleId)
        => $"{BaseUrl}tid/{titleId.ToString(CultureInfo.InvariantCulture)}/time";
}
