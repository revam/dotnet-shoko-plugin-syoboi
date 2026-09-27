using Shoko.Plugin.Syoboi.Mapping;
using Xunit;

namespace Shoko.Plugin.Syoboi.Tests;

/// <summary>
/// The subtitles and comments here are real ones from cal.syoboi.jp.
/// </summary>
public class SyoboiProgramTextTests
{
    [Theory]
    [InlineData("#1～#2", 1, 2)]
    [InlineData("#1〜#2", 1, 2)]
    [InlineData("#1-#2", 1, 2)]
    [InlineData("#23～24", 23, 24)]
    [InlineData("#51～#66", 51, 66)]
    [InlineData("＃３～＃４", 3, 4)]
    [InlineData(" #1～#12 ", 1, 12)]
    public void Reads_an_episode_range(string subTitle, int first, int last)
    {
        Assert.True(SyoboiProgramText.TryParseEpisodeRange(subTitle, out var parsedFirst, out var parsedLast));
        Assert.Equal(first, parsedFirst);
        Assert.Equal(last, parsedLast);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#～#")]
    [InlineData("#14.5 Debriefing")]
    [InlineData("#1～#12+SP#1")]
    [InlineData("#1 静ナル予兆")]
    [InlineData("#2～#1")]
    [InlineData("#0～#1")]
    [InlineData("総集編")]
    public void Reads_no_range_from_anything_else(string? subTitle)
    {
        Assert.False(SyoboiProgramText.TryParseEpisodeRange(subTitle, out _, out _));
    }

    [Theory]
    [InlineData("先行放送")]
    [InlineData("第1話先行放送")]
    [InlineData("先行配信 http://abema.tv/channels/new-anime/slots/9hYgqdrg9etjM9")]
    [InlineData("!週替り枠より先行して放送")]
    [InlineData("WIXOSSクリスマスSP『WIXOSS DIVA(A)LIVE』ちょっと先に見せちゃうぞ！")]
    public void Marks_an_advance_airing(string comment)
    {
        Assert.True(SyoboiProgramText.IsAdvanceAiring(comment));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("!2話連続放送")]
    [InlineData("!野球中継延長のため繰り下げ")]
    [InlineData("!他局の放送休止を受けて先週と同じ話数を放送")]
    [InlineData("本編内で「GOSICK -ゴシック-」の先行映像放送あり")]
    public void Marks_nothing_else_as_an_advance_airing(string? comment)
    {
        Assert.False(SyoboiProgramText.IsAdvanceAiring(comment));
    }
}
