using Shoko.Plugin.Syoboi.Mapping;
using Shoko.Plugin.Syoboi.Models;
using Xunit;

namespace Shoko.Plugin.Syoboi.Tests;

public class SyoboiProgramFilterTests
{
    private static SyoboiProgramEntry MakeEntry(int? count = 1, int flag = 0, bool deleted = false)
        => new(PID: "1", TID: 6309, ChID: 1, StTime: "20220409230000", StOffset: 0, EdTime: "20220409233000", Count: count, Flag: flag, Deleted: deleted);

    [Fact]
    public void A_normal_entry_is_kept()
    {
        Assert.False(SyoboiProgramFilter.ShouldSkip(MakeEntry()));
    }

    [Fact]
    public void A_deleted_entry_is_skipped()
    {
        Assert.True(SyoboiProgramFilter.ShouldSkip(MakeEntry(deleted: true)));
    }

    [Fact]
    public void An_entry_with_no_episode_number_is_skipped()
    {
        Assert.True(SyoboiProgramFilter.ShouldSkip(MakeEntry(count: null)));
    }

    [Fact]
    public void An_entry_with_a_zero_or_negative_episode_number_is_skipped()
    {
        Assert.True(SyoboiProgramFilter.ShouldSkip(MakeEntry(count: 0)));
        Assert.True(SyoboiProgramFilter.ShouldSkip(MakeEntry(count: -1)));
    }

    [Fact]
    public void A_rerun_entry_is_skipped()
    {
        Assert.True(SyoboiProgramFilter.ShouldSkip(MakeEntry(flag: 0x08)));
    }

    [Fact]
    public void A_rerun_flag_combined_with_other_bits_is_still_skipped()
    {
        Assert.True(SyoboiProgramFilter.ShouldSkip(MakeEntry(flag: 0x08 | 0x01)));
    }

    [Fact]
    public void Other_flag_bits_alone_do_not_cause_a_skip()
    {
        Assert.False(SyoboiProgramFilter.ShouldSkip(MakeEntry(flag: 0x01)));
    }
}
