using Shoko.Plugin.Syoboi.Mapping;
using Xunit;

namespace Shoko.Plugin.Syoboi.Tests;

public class SyoboiTimeConverterTests
{
    [Fact]
    public void ToUtc_converts_a_response_timestamp_from_JST_to_UTC()
    {
        // The shape db.php actually answers with, e.g.
        // <StTime>2021-03-30 02:10:00</StTime> for TID 5877 on ChID 70.
        var result = SyoboiTimeConverter.ToUtc("2021-03-30 02:10:00");

        Assert.Equal(new DateTime(2021, 3, 29, 17, 10, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void ToUtc_handles_a_slot_in_the_small_hours_JST()
    {
        // 00:30 JST on the 10th is still 15:30 UTC on the 9th.
        var result = SyoboiTimeConverter.ToUtc("2022-04-10 00:30:00");

        Assert.Equal(new DateTime(2022, 4, 9, 15, 30, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void ToUtc_also_accepts_the_compact_form_Syoboi_uses_elsewhere()
    {
        var result = SyoboiTimeConverter.ToUtc("20220409230000");

        Assert.Equal(new DateTime(2022, 4, 9, 14, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void ToUtc_returns_UTC_kind()
    {
        var result = SyoboiTimeConverter.ToUtc("2022-04-09 23:00:00");

        Assert.Equal(DateTimeKind.Utc, result!.Value.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-date")]
    [InlineData("2022-04-09")]
    [InlineData("2022-04-09T23:00:00Z")]
    public void ToUtc_returns_null_for_unparsable_input(string? input)
    {
        Assert.Null(SyoboiTimeConverter.ToUtc(input));
    }

    [Fact]
    public void ToRange_formats_both_bounds_in_Japanese_local_time()
    {
        var range = SyoboiTimeConverter.ToRange(
            new DateTime(2021, 2, 28, 15, 0, 0, DateTimeKind.Utc),
            new DateTime(2021, 3, 31, 15, 0, 0, DateTimeKind.Utc)
        );

        // The range the live service accepts: bounds are JST, and both are
        // required — the keyword ranges some clients send are rejected with a
        // 400.
        Assert.Equal("20210301_000000-20210401_000000", range);
    }

    [Fact]
    public void ToRange_rejects_a_window_that_ends_before_it_starts()
    {
        var from = new DateTime(2021, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Throws<ArgumentOutOfRangeException>(() => SyoboiTimeConverter.ToRange(from, from));
    }
}
