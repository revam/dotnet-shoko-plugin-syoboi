using System;
using System.Globalization;

namespace Shoko.Plugin.Syoboi.Mapping;

/// <summary>
/// Converts between UTC and Syoboi's own time format: Japan Standard Time
/// with no zone marker, written <c>yyyy-MM-dd HH:mm:ss</c> in a response and
/// <c>yyyyMMdd_HHmmss</c> in a <c>Range</c> request parameter.
/// </summary>
/// <remarks>
/// Japan has no daylight saving time, so the conversion is a fixed +09:00
/// shift (<see cref="SyoboiConstants.TimeZoneOffset"/>) and needs no time zone
/// database lookup.
/// </remarks>
public static class SyoboiTimeConverter
{
    // The response format is what db.php answers with. The compact form is
    // accepted too because Syoboi's other endpoints (rss.php, iEPG) write the
    // same timestamps without separators.
    private static readonly string[] _timeFormats = [SyoboiConstants.TimeFormat, "yyyyMMddHHmmss"];

    /// <summary>
    /// Converts a Syoboi <c>StTime</c>/<c>EdTime</c> value to UTC.
    /// </summary>
    /// <remarks>
    /// A slot's <c>StOffset</c> is deliberately not applied: Syoboi records
    /// the delay in <c>StOffset</c> <em>and</em> writes the delayed time into
    /// <c>StTime</c>, so adding it would count it twice. See
    /// <see cref="Shoko.Plugin.Syoboi.Models.SyoboiProgramEntry.OriginalStartedAt"/>
    /// for the slot the run normally occupies.
    /// </remarks>
    /// <param name="syoboiTime">The time as Syoboi gives it, or <c>null</c>/empty.</param>
    /// <returns>The UTC time, or <c>null</c> if <paramref name="syoboiTime"/> can't be parsed.</returns>
    public static DateTime? ToUtc(string? syoboiTime)
    {
        if (string.IsNullOrWhiteSpace(syoboiTime))
            return null;

        if (!DateTime.TryParseExact(syoboiTime.Trim(), _timeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var japanLocal))
            return null;

        return DateTime.SpecifyKind(japanLocal - SyoboiConstants.TimeZoneOffset, DateTimeKind.Utc);
    }

    /// <summary>
    /// Formats a UTC instant as one bound of a <c>Range</c> request parameter,
    /// in Japanese local time.
    /// </summary>
    /// <param name="utc">The instant to format. Treated as UTC whatever its <see cref="DateTime.Kind"/>.</param>
    /// <returns>The bound, e.g. <c>20210401_000000</c>.</returns>
    public static string ToRangeBound(DateTime utc)
        => (utc + SyoboiConstants.TimeZoneOffset).ToString(SyoboiConstants.RangeBoundFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats a UTC window as a <c>Range</c> request parameter. Syoboi
    /// requires both bounds and matches every slot where
    /// <c>from &lt; EdTime AND StTime &lt; to</c>.
    /// </summary>
    /// <param name="fromUtc">The start of the window, in UTC.</param>
    /// <param name="toUtc">The end of the window, in UTC. Must be after <paramref name="fromUtc"/>.</param>
    /// <returns>The range, e.g. <c>20210301_000000-20210401_000000</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="toUtc"/> is not after <paramref name="fromUtc"/>.
    /// </exception>
    public static string ToRange(DateTime fromUtc, DateTime toUtc)
    {
        if (toUtc <= fromUtc)
            throw new ArgumentOutOfRangeException(nameof(toUtc), toUtc, "The end of a Syoboi range must be after its start.");

        return $"{ToRangeBound(fromUtc)}-{ToRangeBound(toUtc)}";
    }
}
