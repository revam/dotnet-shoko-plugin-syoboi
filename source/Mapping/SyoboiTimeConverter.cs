using System;
using System.Globalization;

namespace Shoko.Plugin.Syoboi.Mapping;

/// <summary>
/// Converts Syoboi's own time format (Japan Standard Time,
/// <c>yyyyMMddHHmmss</c>, no zone marker, plus a separate offset in seconds)
/// into UTC.
/// </summary>
public static class SyoboiTimeConverter
{
    private static readonly TimeSpan JstOffset = TimeSpan.FromHours(9);

    private const string SyoboiDateFormat = "yyyyMMddHHmmss";

    /// <summary>
    /// Converts a Syoboi <c>StTime</c>/<c>EdTime</c> value, plus its
    /// <c>StOffset</c> in seconds, to UTC.
    /// </summary>
    /// <param name="syoboiTime">The time as Syoboi gives it, or <c>null</c>/empty.</param>
    /// <param name="offsetSeconds">Extra seconds to add before converting.</param>
    /// <returns>The UTC time, or <c>null</c> if <paramref name="syoboiTime"/> can't be parsed.</returns>
    public static DateTime? ToUtc(string? syoboiTime, int offsetSeconds = 0)
    {
        if (string.IsNullOrWhiteSpace(syoboiTime))
            return null;

        if (!DateTime.TryParseExact(syoboiTime, SyoboiDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var jstLocal))
            return null;

        var adjusted = jstLocal.AddSeconds(offsetSeconds) - JstOffset;
        return DateTime.SpecifyKind(adjusted, DateTimeKind.Utc);
    }
}
