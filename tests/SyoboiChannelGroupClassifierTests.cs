using Shoko.Abstractions.Metadata.Airing;
using Shoko.Plugin.Syoboi.Mapping;
using Xunit;

namespace Shoko.Plugin.Syoboi.Tests;

public class SyoboiChannelGroupClassifierTests
{
    [Theory]
    [InlineData("ラジオ")]
    [InlineData("Radio")]
    [InlineData("インターネットラジオ")]
    public void Radio_groups_are_skipped(string groupName)
    {
        Assert.Null(SyoboiChannelGroupClassifier.Classify(groupName));
    }

    [Theory]
    [InlineData("ネット配信")]
    [InlineData("ネット")]
    [InlineData("配信")]
    [InlineData("Streaming")]
    public void Internet_groups_are_streaming(string groupName)
    {
        Assert.Equal(AiringChannelType.Streaming, SyoboiChannelGroupClassifier.Classify(groupName));
    }

    [Theory]
    [InlineData("ＴＶ（東京）")]
    [InlineData("ＢＳ／ＣＳ")]
    [InlineData("地上波")]
    public void TV_and_BS_CS_groups_are_television(string groupName)
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
