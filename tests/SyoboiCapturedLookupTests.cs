using Shoko.Abstractions.Metadata.Airing;
using Shoko.Plugin.Syoboi.Http;
using Shoko.Plugin.Syoboi.Mapping;
using Shoko.Plugin.Syoboi.Models;
using Xunit;

namespace Shoko.Plugin.Syoboi.Tests;

/// <summary>
/// Maps whole <c>ProgLookup</c> answers captured from cal.syoboi.jp, against
/// the site's real channel directory, onto an anime numbered like the title.
/// </summary>
public class SyoboiCapturedLookupTests
{
    private static readonly Lazy<SyoboiChannelDirectory> _directory = new(() => new(
        SyoboiResponseParser.ParseChannels(Fixture("chlookup.xml")).Value,
        SyoboiResponseParser.ParseChannelGroups(Fixture("chgrouplookup.xml")).Value
    ));

    // Episode n is AniDB episode 100000 + n.
    private static readonly IReadOnlyDictionary<int, int> _episodes = Enumerable.Range(1, 30).ToDictionary(number => number, number => 100_000 + number);

    private static string Fixture(string fileName)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));

    private static SyoboiLookupResult Lookup(int titleId)
        => new(SyoboiResponseParser.ParsePrograms(Fixture($"proglookup-tid-{titleId}.xml")).Value, _directory.Value);

    private static IReadOnlyList<SyoboiEpisodeAiringDraft> Drafts(int titleId)
        => [.. SyoboiScheduleMapper.BuildChannelBundles(titleId, Lookup(titleId)).SelectMany(bundle => SyoboiScheduleMapper.BuildEpisodeDrafts(bundle.Programs, _episodes))];

    #region Episode ranges

    [Fact]
    public void Reads_the_episode_range_of_a_slot_with_no_count()
    {
        var entry = Assert.Single(Lookup(5518).Programs, entry => entry.PID == "506143");

        Assert.Null(entry.Count);
        Assert.Equal("#1〜#2", entry.SubTitle);
        Assert.Equal((1, 2), entry.EpisodeRange);
    }

    [Theory]
    [InlineData("506143")] // AT-X, #1〜#2
    [InlineData("506638")] // テレビ愛知, #1～#2, 2話連続放送
    [InlineData("512479")] // バンダイチャンネル, #1～#2, 2話同時配信
    public void A_double_premiere_airs_both_episodes_in_one_slot(string pid)
    {
        var entry = Assert.Single(Lookup(5518).Programs, entry => entry.PID == pid);

        var drafts = Drafts(5518).Where(draft => draft.PID == pid).ToList();

        Assert.Equal([1, 2], drafts.Select(draft => draft.EpisodeNumber));
        Assert.Equal([100_001, 100_002], drafts.Select(draft => draft.AnidbEpisodeID));
        Assert.Equal([$"{pid}:1", $"{pid}:2"], drafts.Select(draft => draft.Key));
        Assert.All(drafts, draft => Assert.Equal(entry.StartedAt, draft.AiredAtUtc));
    }

    [Fact]
    public void A_delayed_double_slot_delays_both_episodes()
    {
        // MBS, #23～24 with no second hash, pushed back half an hour.
        var drafts = Drafts(2500).Where(draft => draft.PID == "218115").ToList();

        Assert.Equal([23, 24], drafts.Select(draft => draft.EpisodeNumber));
        Assert.All(drafts, draft =>
        {
            Assert.True(draft.IsDelayed);
            Assert.Equal(new DateTime(2012, 6, 16, 17, 58, 0, DateTimeKind.Utc), draft.AiredAtUtc);
            Assert.Equal(new DateTime(2012, 6, 16, 17, 28, 0, DateTimeKind.Utc), draft.OriginalAiredAtUtc);
        });
    }

    [Fact]
    public void Consecutive_double_slots_each_air_their_own_pair()
    {
        // ぎふチャン, three nights of 2話連続放送.
        var drafts = Drafts(5534).Where(draft => draft.PID is "514000" or "514002" or "514003").ToList();

        Assert.Equal([2, 3, 4, 5, 6, 7], drafts.Select(draft => draft.EpisodeNumber));
    }

    [Fact]
    public void A_rerun_marathon_airs_every_episode_as_a_rerun()
    {
        // Every #1～#12 block for this title is flagged 再.
        var marathons = Lookup(5534).Programs.Where(entry => entry.EpisodeRange is (1, 12)).ToList();

        Assert.NotEmpty(marathons);
        Assert.All(marathons, entry => Assert.True(entry.IsRerun));

        var drafts = Drafts(5534);
        foreach (var marathon in marathons)
        {
            var reruns = drafts.Where(draft => draft.PID == marathon.PID).ToList();
            Assert.Equal(Enumerable.Range(1, 12), reruns.Select(draft => draft.EpisodeNumber));
            Assert.All(reruns, draft => Assert.Equal(EpisodeAiringKind.Rerun, draft.Kind));
        }
    }

    [Fact]
    public void A_rerun_keys_apart_from_the_regular_showing()
    {
        var drafts = Drafts(2092);

        // テレビ東京, episode 3 on 2011-04-18 and its 再 in August.
        var regular = Assert.Single(drafts, draft => draft.PID == "187595");
        var rerun = Assert.Single(drafts, draft => draft.PID == "196765");

        Assert.Equal(EpisodeAiringKind.Normal, regular.Kind);
        Assert.Equal(EpisodeAiringKind.Rerun, rerun.Kind);
        Assert.Equal(regular.AnidbEpisodeID, rerun.AnidbEpisodeID);
        Assert.NotEqual(regular.Key, rerun.Key);
    }

    #endregion

    #region Advance airings

    [Fact]
    public void Marks_an_advance_airing_from_its_comment()
    {
        var drafts = Drafts(2092);

        // サンテレビジョン, 先行放送 of episode 3.
        var advance = Assert.Single(drafts, draft => draft.PID == "182058");
        Assert.Equal(EpisodeAiringKind.Advance, advance.Kind);
        Assert.Equal(3, advance.EpisodeNumber);

        // Its regular showing on the same channel, three months on.
        var regular = Assert.Single(drafts, draft => draft.PID == "187569");
        Assert.Equal(EpisodeAiringKind.Normal, regular.Kind);
        Assert.Equal(advance.AnidbEpisodeID, regular.AnidbEpisodeID);
        Assert.NotEqual(advance.Key, regular.Key);
    }

    #endregion
}
