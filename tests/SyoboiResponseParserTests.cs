using Shoko.Plugin.Syoboi.Http;
using Xunit;

namespace Shoko.Plugin.Syoboi.Tests;

public class SyoboiResponseParserTests
{
    private const string SampleResponse = """
    {
        "Programs": {
            "1000001": { "PID": "1000001", "TID": "6309", "StTime": "20220409230000", "StOffset": "0", "EdTime": "20220409233000", "ChID": "1", "Count": "1", "Flag": "0", "Deleted": "0" },
            "1000002": { "PID": "1000002", "TID": "6309", "StTime": "20220409233000", "StOffset": "0", "EdTime": "20220410000000", "ChID": "2", "Count": "1", "Flag": "0", "Deleted": "0" },
            "1000003": { "PID": "1000003", "TID": "6309", "StTime": "20220416230000", "StOffset": "0", "EdTime": "20220416233000", "ChID": "1", "Count": "1", "Flag": "8", "Deleted": "0" }
        },
        "ChLookup": {
            "1": { "ChID": "1", "ChName": "TOKYO MX", "ChiEPGName": "ＭＸテレビ", "ChGID": "1" },
            "2": { "ChID": "2", "ChName": "Netflix", "ChGID": "6" }
        },
        "ChGroupLookup": {
            "1": { "ChGID": "1", "ChGName": "ＴＶ（東京）" },
            "6": { "ChGID": "6", "ChGName": "ネット配信" }
        }
    }
    """;

    [Fact]
    public void Parses_every_program_entry()
    {
        var result = SyoboiResponseParser.Parse(SampleResponse);

        Assert.Equal(3, result.Programs.Count);
        var first = result.Programs.Single(p => p.PID == "1000001");
        Assert.Equal(6309, first.TID);
        Assert.Equal(1, first.ChID);
        Assert.Equal(1, first.Count);
        Assert.Equal("20220409230000", first.StTime);
        Assert.False(first.Deleted);
    }

    [Fact]
    public void Parses_the_rerun_flag_as_a_number_rather_than_dropping_the_entry()
    {
        // SyoboiResponseParser only converts types; SyoboiProgramFilter is
        // what decides what a rerun flag means.
        var result = SyoboiResponseParser.Parse(SampleResponse);

        var rerun = result.Programs.Single(p => p.PID == "1000003");
        Assert.Equal(8, rerun.Flag);
    }

    [Fact]
    public void Parses_channels_keyed_by_ChID()
    {
        var result = SyoboiResponseParser.Parse(SampleResponse);

        Assert.Equal(2, result.Channels.Count);
        Assert.Equal("TOKYO MX", result.Channels[1].ChName);
        Assert.Equal("ＭＸテレビ", result.Channels[1].ChiEPGName);
        Assert.Equal(1, result.Channels[1].ChGID);
        Assert.Null(result.Channels[2].ChiEPGName);
    }

    [Fact]
    public void Parses_channel_groups_keyed_by_ChGID()
    {
        var result = SyoboiResponseParser.Parse(SampleResponse);

        Assert.Equal(2, result.ChannelGroups.Count);
        Assert.Equal("ネット配信", result.ChannelGroups[6].ChGName);
    }

    [Fact]
    public void Returns_empty_for_malformed_JSON()
    {
        var result = SyoboiResponseParser.Parse("{ not json");

        Assert.Empty(result.Programs);
        Assert.Empty(result.Channels);
        Assert.Empty(result.ChannelGroups);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Returns_empty_for_blank_input(string json)
    {
        var result = SyoboiResponseParser.Parse(json);

        Assert.Empty(result.Programs);
    }

    [Fact]
    public void Returns_empty_when_the_document_is_a_bare_JSON_null()
    {
        var result = SyoboiResponseParser.Parse("null");

        Assert.Empty(result.Programs);
    }

    [Fact]
    public void Tolerates_a_response_missing_a_whole_section()
    {
        var result = SyoboiResponseParser.Parse("""{ "Programs": {} }""");

        Assert.Empty(result.Programs);
        Assert.Empty(result.Channels);
        Assert.Empty(result.ChannelGroups);
    }

    [Fact]
    public void Skips_a_program_entry_missing_its_TID()
    {
        var result = SyoboiResponseParser.Parse("""
        {
            "Programs": {
                "1": { "PID": "1", "ChID": "1", "StTime": "20220409230000", "Count": "1" }
            }
        }
        """);

        Assert.Empty(result.Programs);
    }

    [Fact]
    public void Skips_a_channel_entry_missing_its_name()
    {
        var result = SyoboiResponseParser.Parse("""
        {
            "ChLookup": {
                "1": { "ChID": "1", "ChGID": "1" }
            }
        }
        """);

        Assert.Empty(result.Channels);
    }

    [Fact]
    public void Falls_back_to_the_dictionary_key_when_an_ID_field_is_missing()
    {
        var result = SyoboiResponseParser.Parse("""
        {
            "Programs": {
                "1000001": { "TID": "6309", "ChID": "1", "StTime": "20220409230000", "Count": "1" }
            }
        }
        """);

        Assert.Equal("1000001", Assert.Single(result.Programs).PID);
    }
}
