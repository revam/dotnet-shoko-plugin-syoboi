using Shoko.Abstractions.Metadata.Airing;
using Shoko.Plugin.Syoboi.Mapping;
using Shoko.Plugin.Syoboi.Models;
using Xunit;

namespace Shoko.Plugin.Syoboi.Tests;

public class SyoboiScheduleMapperTests
{
    private const int TitleId = 6309;

    private static SyoboiProgramEntry Program(string pid, int chId, int? count = 1, string stTime = "20220409230000", int flag = 0, bool deleted = false)
        => new(pid, TitleId, chId, stTime, 0, "20220409233000", count, flag, deleted);

    [Fact]
    public void Groups_programs_by_channel_into_one_bundle_each()
    {
        var lookup = new SyoboiLookupResult(
            Programs: [Program("1", chId: 1), Program("2", chId: 2)],
            Channels: new Dictionary<int, SyoboiChannel>
            {
                [1] = new(1, "TOKYO MX", "ＭＸテレビ", ChGID: 1),
                [2] = new(2, "BS11", null, ChGID: 1),
            },
            ChannelGroups: new Dictionary<int, SyoboiChannelGroup> { [1] = new(1, "ＴＶ（東京）") }
        );

        var bundles = SyoboiScheduleMapper.BuildChannelBundles(TitleId, lookup);

        Assert.Equal(2, bundles.Count);
        Assert.Contains(bundles, b => b.ChID == 1 && b.ChannelName == "TOKYO MX" && b.ChannelType == AiringChannelType.Television);
        Assert.Contains(bundles, b => b.ChID == 2 && b.ChannelName == "BS11");
    }

    [Fact]
    public void Ignores_programs_for_other_titles()
    {
        var lookup = new SyoboiLookupResult(
            Programs: [Program("1", chId: 1) with { TID = 9999 }],
            Channels: new Dictionary<int, SyoboiChannel> { [1] = new(1, "TOKYO MX", null, 1) },
            ChannelGroups: new Dictionary<int, SyoboiChannelGroup> { [1] = new(1, "ＴＶ（東京）") }
        );

        var bundles = SyoboiScheduleMapper.BuildChannelBundles(TitleId, lookup);

        Assert.Empty(bundles);
    }

    [Fact]
    public void Drops_reruns_and_deleted_entries_before_grouping()
    {
        var lookup = new SyoboiLookupResult(
            Programs: [Program("1", chId: 1, flag: 0x08), Program("2", chId: 1, deleted: true)],
            Channels: new Dictionary<int, SyoboiChannel> { [1] = new(1, "TOKYO MX", null, 1) },
            ChannelGroups: new Dictionary<int, SyoboiChannelGroup> { [1] = new(1, "ＴＶ（東京）") }
        );

        var bundles = SyoboiScheduleMapper.BuildChannelBundles(TitleId, lookup);

        Assert.Empty(bundles);
    }

    [Fact]
    public void Radio_channel_groups_are_excluded_entirely()
    {
        var lookup = new SyoboiLookupResult(
            Programs: [Program("1", chId: 1)],
            Channels: new Dictionary<int, SyoboiChannel> { [1] = new(1, "文化放送", null, 9) },
            ChannelGroups: new Dictionary<int, SyoboiChannelGroup> { [9] = new(9, "ラジオ") }
        );

        var bundles = SyoboiScheduleMapper.BuildChannelBundles(TitleId, lookup);

        Assert.Empty(bundles);
    }

    [Fact]
    public void An_allow_list_restricts_which_channel_groups_are_included()
    {
        var lookup = new SyoboiLookupResult(
            Programs: [Program("1", chId: 1), Program("2", chId: 2)],
            Channels: new Dictionary<int, SyoboiChannel>
            {
                [1] = new(1, "TOKYO MX", null, 1),
                [2] = new(2, "Netflix", null, 6),
            },
            ChannelGroups: new Dictionary<int, SyoboiChannelGroup>
            {
                [1] = new(1, "ＴＶ（東京）"),
                [6] = new(6, "ネット配信"),
            }
        );

        var bundles = SyoboiScheduleMapper.BuildChannelBundles(TitleId, lookup, allowedChannelGroupNames: new HashSet<string> { "ネット配信" });

        var bundle = Assert.Single(bundles);
        Assert.Equal(2, bundle.ChID);
    }

    [Fact]
    public void An_international_streaming_brand_is_registered_as_a_regional_channel()
    {
        var lookup = new SyoboiLookupResult(
            Programs: [Program("1", chId: 2)],
            Channels: new Dictionary<int, SyoboiChannel> { [2] = new(2, "Netflix", null, 6) },
            ChannelGroups: new Dictionary<int, SyoboiChannelGroup> { [6] = new(6, "ネット配信") }
        );

        var bundle = Assert.Single(SyoboiScheduleMapper.BuildChannelBundles(TitleId, lookup));

        Assert.Equal("Netflix (JP)", bundle.ChannelName);
        Assert.Equal(AiringChannelType.Streaming, bundle.ChannelType);
    }

    [Fact]
    public void The_EPG_name_becomes_an_alias_when_it_differs_from_the_display_name()
    {
        var lookup = new SyoboiLookupResult(
            Programs: [Program("1", chId: 1)],
            Channels: new Dictionary<int, SyoboiChannel> { [1] = new(1, "TOKYO MX", "ＭＸテレビ", 1) },
            ChannelGroups: new Dictionary<int, SyoboiChannelGroup> { [1] = new(1, "ＴＶ（東京）") }
        );

        var bundle = Assert.Single(SyoboiScheduleMapper.BuildChannelBundles(TitleId, lookup));

        Assert.Equal(["ＭＸテレビ"], bundle.ChannelAliases);
    }

    [Fact]
    public void A_channel_with_no_matching_lookup_entry_is_skipped()
    {
        var lookup = new SyoboiLookupResult(
            Programs: [Program("1", chId: 404)],
            Channels: new Dictionary<int, SyoboiChannel>(),
            ChannelGroups: new Dictionary<int, SyoboiChannelGroup>()
        );

        Assert.Empty(SyoboiScheduleMapper.BuildChannelBundles(TitleId, lookup));
    }

    [Fact]
    public void BuildEpisodeDrafts_maps_entries_onto_known_episode_numbers()
    {
        var programs = new[] { Program("100", chId: 1, count: 1), Program("101", chId: 1, count: 2) };
        var episodeIds = new Dictionary<int, int> { [1] = 555, [2] = 556 };

        var drafts = SyoboiScheduleMapper.BuildEpisodeDrafts(programs, episodeIds);

        Assert.Equal(2, drafts.Count);
        var first = drafts.Single(d => d.PID == "100");
        Assert.Equal(555, first.AnidbEpisodeID);
        Assert.Equal("100:1", first.Key);
        Assert.NotNull(first.AiredAtUtc);
    }

    [Fact]
    public void BuildEpisodeDrafts_skips_entries_with_no_matching_episode_number()
    {
        var programs = new[] { Program("100", chId: 1, count: 99) };
        var episodeIds = new Dictionary<int, int> { [1] = 555 };

        Assert.Empty(SyoboiScheduleMapper.BuildEpisodeDrafts(programs, episodeIds));
    }
}
