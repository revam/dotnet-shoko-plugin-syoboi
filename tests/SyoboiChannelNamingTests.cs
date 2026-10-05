using Shoko.Abstractions.Metadata.Airing;
using Shoko.Plugin.Syoboi.Mapping;
using Xunit;

namespace Shoko.Plugin.Syoboi.Tests;

public class SyoboiChannelNamingTests
{
    [Theory]
    [InlineData("Netflix")]
    [InlineData("Amazon Prime Video")]
    [InlineData("  Netflix  ")]
    public void A_global_streaming_brand_is_registered_without_a_country(string channelName)
    {
        Assert.Null(SyoboiChannelNaming.ResolveCountryCode(channelName, AiringChannelType.Streaming));
    }

    [Theory]
    [InlineData("ABEMA")]
    [InlineData("dアニメストア")]
    [InlineData("Hulu")]
    public void A_streaming_service_only_available_in_japan_is_registered_in_japan(string channelName)
    {
        Assert.Equal("JP", SyoboiChannelNaming.ResolveCountryCode(channelName, AiringChannelType.Streaming));
    }

    [Theory]
    [InlineData("TOKYO MX")]
    [InlineData("Netflix")]
    public void A_television_station_is_registered_in_japan(string channelName)
    {
        // Syoboi should never classify Netflix as TV, but a station keeps its
        // country whatever it is called.
        Assert.Equal("JP", SyoboiChannelNaming.ResolveCountryCode(channelName, AiringChannelType.Television));
    }

    [Fact]
    public void The_name_is_kept_as_syoboi_gives_it_but_trimmed()
    {
        Assert.Equal("Netflix", SyoboiChannelNaming.ResolveDisplayName("  Netflix  "));
    }
}
