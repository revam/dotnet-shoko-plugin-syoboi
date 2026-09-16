using Shoko.Abstractions.Metadata.Airing;
using Shoko.Plugin.Syoboi.Mapping;
using Shoko.Plugin.Syoboi.Models;
using Xunit;

namespace Shoko.Plugin.Syoboi.Tests;

/// <summary>
/// The IDs and names used here are cal.syoboi.jp's real ones: TID 5877 on
/// ChID 19 (TOKYO MX, group 1 テレビ 関東), 128 (BS11イレブン, group 2
/// BSデジタル) and 256 (Netflix, group 7 インターネット).
/// </summary>
public class SyoboiScheduleMapperTests
{
    private const int TitleId = 5877;

    private static readonly DateTime _startedAt = new(2021, 3, 29, 17, 10, 0, DateTimeKind.Utc);

    private static SyoboiProgramEntry Program(string pid, int chId, int? count = 1, int flag = 0, bool deleted = false, int stOffset = 0)
        => new(pid, TitleId, chId, _startedAt, _startedAt.AddMinutes(30), stOffset, count, flag, deleted);

    private static SyoboiLookupResult Lookup(IReadOnlyList<SyoboiProgramEntry> programs, params SyoboiChannel[] channels)
        => new(programs, new SyoboiChannelDirectory(
            channels.ToDictionary(channel => channel.ChID),
            new Dictionary<int, SyoboiChannelGroup>
            {
                [1] = new(1, "テレビ 関東"),
                [2] = new(2, "BSデジタル"),
                [7] = new(7, "インターネット"),
                [10] = new(10, "ラジオ 全国"),
            }
        ));

    private static readonly SyoboiChannel _tokyoMx = new(19, "TOKYO MX", "ＭＸテレビ", ChGID: 1);

    private static readonly SyoboiChannel _bs11 = new(128, "BS11イレブン", "ＢＳ１１", ChGID: 2);

    private static readonly SyoboiChannel _netflix = new(256, "Netflix", null, ChGID: 7);

    [Fact]
    public void Groups_programs_by_channel_into_one_bundle_each()
    {
        var lookup = Lookup([Program("1", chId: 19), Program("2", chId: 128)], _tokyoMx, _bs11);

        var bundles = SyoboiScheduleMapper.BuildChannelBundles(TitleId, lookup);

        Assert.Equal(2, bundles.Count);
        Assert.Contains(bundles, bundle => bundle.ChID == 19 && bundle.ChannelName == "TOKYO MX" && bundle.ChannelType == AiringChannelType.Television);
        Assert.Contains(bundles, bundle => bundle.ChID == 128 && bundle.ChannelName == "BS11イレブン");
        Assert.All(bundles, bundle => Assert.Equal(TitleId, bundle.TitleID));
    }

    [Fact]
    public void Ignores_programs_for_other_titles()
    {
        var lookup = Lookup([Program("1", chId: 19) with { TID = 5878 }], _tokyoMx);

        Assert.Empty(SyoboiScheduleMapper.BuildChannelBundles(TitleId, lookup));
    }

    [Fact]
    public void Drops_reruns_and_deleted_entries_before_grouping()
    {
        var lookup = Lookup(
            [Program("1", chId: 19, flag: (int)SyoboiProgramFlags.Rerun), Program("2", chId: 19, deleted: true)],
            _tokyoMx
        );

        Assert.Empty(SyoboiScheduleMapper.BuildChannelBundles(TitleId, lookup));
    }

    [Fact]
    public void Keeps_the_final_episode_of_a_run()
    {
        var lookup = Lookup([Program("1", chId: 19, flag: (int)SyoboiProgramFlags.FinalEpisode)], _tokyoMx);

        Assert.Single(SyoboiScheduleMapper.BuildChannelBundles(TitleId, lookup));
    }

    [Fact]
    public void Radio_channel_groups_are_excluded_entirely()
    {
        var lookup = Lookup([Program("1", chId: 42)], new SyoboiChannel(42, "文化放送", null, ChGID: 10));

        Assert.Empty(SyoboiScheduleMapper.BuildChannelBundles(TitleId, lookup));
    }

    [Fact]
    public void An_allow_list_restricts_which_channel_groups_are_included()
    {
        var lookup = Lookup([Program("1", chId: 19), Program("2", chId: 256)], _tokyoMx, _netflix);

        var bundles = SyoboiScheduleMapper.BuildChannelBundles(TitleId, lookup, allowedChannelGroupNames: new HashSet<string> { "インターネット" });

        Assert.Equal(256, Assert.Single(bundles).ChID);
    }

    [Fact]
    public void An_international_streaming_brand_is_registered_as_a_regional_channel()
    {
        var lookup = Lookup([Program("1", chId: 256)], _netflix);

        var bundle = Assert.Single(SyoboiScheduleMapper.BuildChannelBundles(TitleId, lookup));

        Assert.Equal("Netflix (JP)", bundle.ChannelName);
        Assert.Equal(AiringChannelType.Streaming, bundle.ChannelType);
    }

    [Fact]
    public void The_EPG_name_becomes_an_alias_when_it_differs_from_the_display_name()
    {
        var lookup = Lookup([Program("1", chId: 19)], _tokyoMx);

        var bundle = Assert.Single(SyoboiScheduleMapper.BuildChannelBundles(TitleId, lookup));

        Assert.Equal(["ＭＸテレビ"], bundle.ChannelAliases);
    }

    [Fact]
    public void A_channel_with_no_matching_lookup_entry_is_skipped()
    {
        var lookup = Lookup([Program("1", chId: 999)], _tokyoMx);

        Assert.Empty(SyoboiScheduleMapper.BuildChannelBundles(TitleId, lookup));
    }

    [Fact]
    public void BuildEpisodeDrafts_maps_entries_onto_known_episode_numbers()
    {
        var programs = new[] { Program("543959", chId: 19, count: 1), Program("543960", chId: 19, count: 2) };
        var episodeIds = new Dictionary<int, int> { [1] = 555, [2] = 556 };

        var drafts = SyoboiScheduleMapper.BuildEpisodeDrafts(programs, episodeIds);

        Assert.Equal(2, drafts.Count);
        var first = drafts.Single(draft => draft.PID == "543959");
        Assert.Equal(555, first.AnidbEpisodeID);
        Assert.Equal("543959:1", first.Key);
        Assert.Equal(_startedAt, first.AiredAtUtc);
    }

    [Fact]
    public void BuildEpisodeDrafts_carries_a_delay_through_to_the_draft()
    {
        var programs = new[] { Program("538991", chId: 19, count: 1, stOffset: 600) };

        var draft = Assert.Single(SyoboiScheduleMapper.BuildEpisodeDrafts(programs, new Dictionary<int, int> { [1] = 555 }));

        Assert.Equal(_startedAt, draft.AiredAtUtc);
        Assert.Equal(_startedAt.AddSeconds(-600), draft.OriginalAiredAtUtc);
        Assert.True(draft.IsDelayed);
    }

    [Fact]
    public void BuildEpisodeDrafts_pins_an_unnumbered_slot_onto_a_single_episode_anime()
    {
        // A film or special: one broadcast, no <Count>, one episode to be.
        var programs = new[] { Program("354919", chId: 19, count: null) };

        var draft = Assert.Single(SyoboiScheduleMapper.BuildEpisodeDrafts(programs, new Dictionary<int, int> { [1] = 555 }));

        Assert.Equal(1, draft.EpisodeNumber);
        Assert.Equal(555, draft.AnidbEpisodeID);
    }

    [Fact]
    public void BuildEpisodeDrafts_leaves_an_unnumbered_slot_alone_when_the_anime_has_many_episodes()
    {
        var programs = new[] { Program("354919", chId: 19, count: null) };

        Assert.Empty(SyoboiScheduleMapper.BuildEpisodeDrafts(programs, new Dictionary<int, int> { [1] = 555, [2] = 556 }));
    }

    [Fact]
    public void An_unnumbered_slot_only_survives_grouping_when_it_is_allowed_to()
    {
        var lookup = Lookup([Program("354919", chId: 19, count: null)], _tokyoMx);

        Assert.Empty(SyoboiScheduleMapper.BuildChannelBundles(TitleId, lookup));
        Assert.Single(SyoboiScheduleMapper.BuildChannelBundles(TitleId, lookup, allowedChannelGroupNames: null, allowMissingEpisodeNumbers: true));
    }

    [Fact]
    public void BuildEpisodeDrafts_skips_entries_with_no_matching_episode_number()
    {
        var programs = new[] { Program("543959", chId: 19, count: 99) };

        Assert.Empty(SyoboiScheduleMapper.BuildEpisodeDrafts(programs, new Dictionary<int, int> { [1] = 555 }));
    }
}
