using Shoko.Abstractions.Metadata.Airing;
using Shoko.Plugin.Syoboi.Mapping;
using Xunit;

namespace Shoko.Plugin.Syoboi.Tests;

public class SyoboiChannelNamingTests
{
    [Fact]
    public void An_international_streaming_brand_is_registered_as_a_JP_regional_channel()
    {
        var name = SyoboiChannelNaming.ResolveDisplayName("Netflix", AiringChannelType.Streaming);

        Assert.Equal("Netflix (JP)", name);
    }

    [Fact]
    public void An_international_brand_is_not_regionalised_when_classified_as_television()
    {
        // Defensive: Syoboi should never classify Netflix as TV, but if it
        // somehow did, this must not silently rename an actual TV station.
        var name = SyoboiChannelNaming.ResolveDisplayName("Netflix", AiringChannelType.Television);

        Assert.Equal("Netflix", name);
    }

    [Theory]
    [InlineData("TOKYO MX")]
    [InlineData("ABEMA")]
    [InlineData("dアニメストア")]
    public void A_non_international_channel_keeps_its_own_name(string channelName)
    {
        var name = SyoboiChannelNaming.ResolveDisplayName(channelName, AiringChannelType.Streaming);

        Assert.Equal(channelName, name);
    }

    [Fact]
    public void The_name_is_trimmed()
    {
        var name = SyoboiChannelNaming.ResolveDisplayName("  Netflix  ", AiringChannelType.Streaming);

        Assert.Equal("Netflix (JP)", name);
    }
}
