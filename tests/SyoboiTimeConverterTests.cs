using Shoko.Plugin.Syoboi.Mapping;
using Xunit;

namespace Shoko.Plugin.Syoboi.Tests;

public class SyoboiTimeConverterTests
{
    [Fact]
    public void ToUtc_converts_JST_to_UTC()
    {
        // SPY×FAMILY episode 1: 23:00 JST on 2022-04-09, per the airing
        // schedule plan.
        var result = SyoboiTimeConverter.ToUtc("20220409230000", offsetSeconds: 0);

        Assert.Equal(new DateTime(2022, 4, 9, 14, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void ToUtc_applies_the_offset_before_converting()
    {
        var result = SyoboiTimeConverter.ToUtc("20220409230000", offsetSeconds: 1800);

        Assert.Equal(new DateTime(2022, 4, 9, 14, 30, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void ToUtc_handles_a_slot_that_crosses_midnight_JST()
    {
        // 00:30 JST on the 10th is still 15:30 UTC on the 9th.
        var result = SyoboiTimeConverter.ToUtc("20220410003000", offsetSeconds: 0);

        Assert.Equal(new DateTime(2022, 4, 9, 15, 30, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void ToUtc_returns_UTC_kind()
    {
        var result = SyoboiTimeConverter.ToUtc("20220409230000");

        Assert.Equal(DateTimeKind.Utc, result!.Value.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-date")]
    [InlineData("2022-04-09")]
    public void ToUtc_returns_null_for_unparsable_input(string? input)
    {
        Assert.Null(SyoboiTimeConverter.ToUtc(input));
    }
}
