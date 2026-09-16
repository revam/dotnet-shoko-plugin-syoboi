using Shoko.Plugin.Syoboi.Mapping;
using Shoko.Plugin.Syoboi.Models;
using Xunit;

namespace Shoko.Plugin.Syoboi.Tests;

public class SyoboiProgramFilterTests
{
    private static SyoboiProgramEntry MakeEntry(int? count = 1, int flag = 0, bool deleted = false)
        => new(
            PID: "538990",
            TID: 5877,
            ChID: 70,
            StartedAt: new DateTime(2021, 3, 1, 17, 10, 0, DateTimeKind.Utc),
            EndedAt: new DateTime(2021, 3, 1, 17, 40, 0, DateTimeKind.Utc),
            StOffset: 0,
            Count: count,
            Flag: flag,
            Deleted: deleted
        );

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
        Assert.True(SyoboiProgramFilter.ShouldSkip(MakeEntry(flag: (int)SyoboiProgramFlags.Rerun)));
    }

    [Fact]
    public void A_rerun_flag_combined_with_other_bits_is_still_skipped()
    {
        Assert.True(SyoboiProgramFilter.ShouldSkip(MakeEntry(flag: (int)(SyoboiProgramFlags.Rerun | SyoboiProgramFlags.Notice))));
    }

    [Fact]
    public void An_unnumbered_entry_is_kept_when_missing_numbers_are_allowed()
    {
        // Syoboi leaves <Count> empty on a one-off broadcast, e.g. all three
        // slots of TID 4015 (a One Piece TV special) on 2015-12-19.
        Assert.False(SyoboiProgramFilter.ShouldSkip(MakeEntry(count: null), allowMissingEpisodeNumber: true));
    }

    [Fact]
    public void A_zero_episode_number_is_skipped_even_when_missing_numbers_are_allowed()
    {
        Assert.True(SyoboiProgramFilter.ShouldSkip(MakeEntry(count: 0), allowMissingEpisodeNumber: true));
    }

    [Theory]
    [InlineData(SyoboiProgramFlags.Notice)]
    [InlineData(SyoboiProgramFlags.FirstEpisode)]
    [InlineData(SyoboiProgramFlags.FinalEpisode)]
    public void The_other_documented_flags_do_not_cause_a_skip(SyoboiProgramFlags flag)
    {
        // Every slot in a run's last week carries 終 (0x04); dropping those
        // would lose the finale on every channel that aired it.
        Assert.False(SyoboiProgramFilter.ShouldSkip(MakeEntry(flag: (int)flag)));
    }
}
