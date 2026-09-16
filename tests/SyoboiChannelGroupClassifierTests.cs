using Shoko.Abstractions.Metadata.Airing;
using Shoko.Plugin.Syoboi.Mapping;
using Xunit;

namespace Shoko.Plugin.Syoboi.Tests;

/// <summary>
/// The group names below are cal.syoboi.jp's real ones, from
/// <c>db.php?Command=ChGroupLookup</c>: 28 groups, named by region or carrier
/// rather than by kind.
/// </summary>
public class SyoboiChannelGroupClassifierTests
{
    [Theory]
    [InlineData("ラジオ 全国")]
    [InlineData("ラジオ 関東")]
    [InlineData("ラジオ 近畿")]
    [InlineData("インターネットラジオ")]
    public void Radio_groups_are_skipped(string groupName)
    {
        Assert.Null(SyoboiChannelGroupClassifier.Classify(groupName));
    }

    [Theory]
    [InlineData("インターネット")]
    [InlineData("AbemaTV")]
    [InlineData("ネット配信")]
    public void Internet_groups_are_streaming(string groupName)
    {
        Assert.Equal(AiringChannelType.Streaming, SyoboiChannelGroupClassifier.Classify(groupName));
    }

    [Theory]
    [InlineData("テレビ 関東")]
    [InlineData("テレビ 近畿")]
    [InlineData("テレビ 全国")]
    [InlineData("BSデジタル")]
    [InlineData("BSデジタル4K/8K")]
    [InlineData("BSアナログ/デジタル")]
    [InlineData("スカパー")]
    [InlineData("信越放送")]
    public void Terrestrial_and_satellite_groups_are_television(string groupName)
    {
        Assert.Equal(AiringChannelType.Television, SyoboiChannelGroupClassifier.Classify(groupName));
    }

    [Fact]
    public void An_unrecognised_group_defaults_to_television()
    {
        Assert.Equal(AiringChannelType.Television, SyoboiChannelGroupClassifier.Classify("その他"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_group_name_defaults_to_television(string? groupName)
    {
        Assert.Equal(AiringChannelType.Television, SyoboiChannelGroupClassifier.Classify(groupName!));
    }
}
