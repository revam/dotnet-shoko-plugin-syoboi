using Shoko.Plugin.Syoboi.Http;
using Xunit;

namespace Shoko.Plugin.Syoboi.Tests;

/// <summary>
/// Every fixture below is a real cal.syoboi.jp response, captured with the
/// request shown above it and trimmed to a handful of rows (and, for the
/// title, a handful of fields). They are deliberately not hand-written: the
/// service answers in XML whatever a client asks for, and the parser used to
/// be written against an invented JSON shape that no request ever returns.
/// </summary>
public class SyoboiResponseParserTests
{
    // GET /db.php?Command=ProgLookup&TID=5877,5878&Range=20210301_000000-20210401_000000
    // Note that a successful ProgLookup carries no <Result> envelope at all.
    private const string ProgramsResponse = """
    <?xml version="1.0" encoding="UTF-8"?><ProgLookupResponse><ProgItems><ProgItem id="543959"><LastUpdate>2021-02-06 14:51:22</LastUpdate><PID>543959</PID><TID>5877</TID><StTime>2021-03-30 02:10:00</StTime><StOffset>0</StOffset><EdTime>2021-03-30 02:40:00</EdTime><Count>13</Count><SubTitle></SubTitle><ProgComment></ProgComment><Flag>4</Flag><Deleted>0</Deleted><Warn>0</Warn><ChID>70</ChID><Revision>1</Revision></ProgItem><ProgItem id="538505"><LastUpdate>2020-12-20 00:17:50</LastUpdate><PID>538505</PID><TID>5877</TID><StTime>2021-03-30 00:00:00</StTime><StOffset>0</StOffset><EdTime>2021-03-30 00:30:00</EdTime><Count>13</Count><SubTitle></SubTitle><ProgComment></ProgComment><Flag>0</Flag><Deleted>1</Deleted><Warn>0</Warn><ChID>128</ChID><Revision>0</Revision></ProgItem><ProgItem id="538990"><LastUpdate>2021-02-24 00:28:50</LastUpdate><PID>538990</PID><TID>5877</TID><StTime>2021-03-02 02:11:00</StTime><StOffset>60</StOffset><EdTime>2021-03-02 02:41:00</EdTime><Count>9</Count><SubTitle></SubTitle><ProgComment></ProgComment><Flag>0</Flag><Deleted>0</Deleted><Warn>1</Warn><ChID>70</ChID><Revision>1</Revision></ProgItem><ProgItem id="538991"><LastUpdate>2021-03-03 00:45:07</LastUpdate><PID>538991</PID><TID>5877</TID><StTime>2021-03-09 02:20:00</StTime><StOffset>600</StOffset><EdTime>2021-03-09 02:50:00</EdTime><Count>10</Count><SubTitle></SubTitle><ProgComment></ProgComment><Flag>0</Flag><Deleted>0</Deleted><Warn>1</Warn><ChID>70</ChID><Revision>1</Revision></ProgItem></ProgItems></ProgLookupResponse>
    """;

    // GET /db.php?Command=ProgLookup&TID=5877&Range=20210501_000000-20210502_000000
    private const string NoDataResponse = """
    <?xml version="1.0" encoding="UTF-8"?><ProgLookupResponse><Result><Code>404</Code><Message>条件に一致するデータは存在しません</Message></Result></ProgLookupResponse>
    """;

    // GET /db.php?Command=ProgLookup&TID=5877&Range=thisweek2 — the keyword
    // range this client used to send, which the service has never accepted.
    private const string BadRangeResponse = """
    <?xml version="1.0" encoding="UTF-8"?><ProgLookupResponse><Result><Code>400</Code><Message>'#Range' が不正です</Message></Result></ProgLookupResponse>
    """;

    // GET /db.php — with no Command at all the envelope is the whole document.
    private const string BadCommandResponse = """
    <?xml version="1.0" encoding="UTF-8"?><Result><Code>400</Code><Message>Command が指定されていません。</Message></Result>
    """;

    // GET /db.php?Command=ChLookup
    private const string ChannelsResponse = """
    <?xml version="1.0" encoding="UTF-8"?><ChLookupResponse><Result><Code>200</Code><Message></Message></Result><ChItems><ChItem id="19"><LastUpdate>2020-10-11 06:01:33</LastUpdate><ChID>19</ChID><ChName>TOKYO MX</ChName><ChiEPGName>ＭＸテレビ</ChiEPGName><ChURL>https://s.mxtv.jp/</ChURL><ChEPGURL>https://s.mxtv.jp/bangumi/</ChEPGURL><ChComment></ChComment><ChGID>1</ChGID><ChNumber>9</ChNumber></ChItem><ChItem id="128"><LastUpdate>2018-10-18 02:24:59</LastUpdate><ChID>128</ChID><ChName>BS11イレブン</ChName><ChiEPGName>ＢＳ１１</ChiEPGName><ChURL>https://www.bs11.jp/</ChURL><ChEPGURL>https://www.bs11.jp/program/</ChEPGURL><ChComment>2007/12/1～、「BS11デジタル」のチャンネル名で開局
    2011/4/1～、チャンネル名を「BS11」に変更</ChComment><ChGID>2</ChGID><ChNumber>11</ChNumber></ChItem><ChItem id="256"><LastUpdate>2020-01-16 12:27:19</LastUpdate><ChID>256</ChID><ChName>Netflix</ChName><ChiEPGName></ChiEPGName><ChURL>https://www.netflix.com/jp/</ChURL><ChEPGURL>https://www.netflix.com/jp/browse/genre/83</ChEPGURL><ChComment></ChComment><ChGID>7</ChGID><ChNumber></ChNumber></ChItem></ChItems></ChLookupResponse>
    """;

    // GET /db.php?Command=ChGroupLookup
    private const string ChannelGroupsResponse = """
    <?xml version="1.0" encoding="UTF-8"?><ChGroupLookupResponse><Result><Code>200</Code><Message></Message></Result><ChGroupItems><ChGroupItem id="1"><LastUpdate>2010-01-15 21:46:14</LastUpdate><ChGID>1</ChGID><ChGroupName>テレビ 関東</ChGroupName><ChGroupComment></ChGroupComment><ChGroupOrder>1200</ChGroupOrder></ChGroupItem><ChGroupItem id="2"><LastUpdate>2010-01-15 21:44:11</LastUpdate><ChGID>2</ChGID><ChGroupName>BSデジタル</ChGroupName><ChGroupComment></ChGroupComment><ChGroupOrder>3000</ChGroupOrder></ChGroupItem><ChGroupItem id="7"><LastUpdate>2010-01-15 21:44:32</LastUpdate><ChGID>7</ChGID><ChGroupName>インターネット</ChGroupName><ChGroupComment></ChGroupComment><ChGroupOrder>7000</ChGroupOrder></ChGroupItem><ChGroupItem id="10"><LastUpdate>2011-11-20 00:10:28</LastUpdate><ChGID>10</ChGID><ChGroupName>ラジオ 全国</ChGroupName><ChGroupComment></ChGroupComment><ChGroupOrder>6000</ChGroupOrder></ChGroupItem><ChGroupItem id="23"><LastUpdate>2016-06-30 03:29:58</LastUpdate><ChGID>23</ChGID><ChGroupName>AbemaTV</ChGroupName><ChGroupComment></ChGroupComment><ChGroupOrder>7100</ChGroupOrder></ChGroupItem></ChGroupItems></ChGroupLookupResponse>
    """;

    // GET /db.php?Command=TitleLookup&TID=5877 — the Comment, Keywords and
    // SubTitles fields are dropped here; they run to several kilobytes.
    private const string TitlesResponse = """
    <?xml version="1.0" encoding="UTF-8"?><TitleLookupResponse><Result><Code>200</Code><Message></Message></Result><TitleItems><TitleItem id="5877"><TID>5877</TID><LastUpdate>2021-05-14 00:46:08</LastUpdate><Title>ウマ娘 プリティーダービー Season 2</Title><ShortTitle>ウマ娘 プリティーダービー(2)</ShortTitle><TitleYomi>うまむすめぷりてぃーだーびーしーずんつー</TitleYomi><TitleEN></TitleEN><Cat>10</Cat><TitleFlag>0</TitleFlag><FirstYear>2021</FirstYear><FirstMonth>1</FirstMonth><FirstCh>TOKYO MX、BS11</FirstCh></TitleItem></TitleItems></TitleLookupResponse>
    """;

    #region Programs

    [Fact]
    public void Parses_every_program_entry_in_an_envelope_less_ProgLookup_response()
    {
        var response = SyoboiResponseParser.ParsePrograms(ProgramsResponse);

        Assert.Equal(SyoboiResponseStatus.Success, response.Status);
        Assert.Equal(4, response.Value.Count);

        var entry = response.Value.Single(program => program.PID == "543959");
        Assert.Equal(5877, entry.TID);
        Assert.Equal(70, entry.ChID);
        Assert.Equal(13, entry.Count);
        Assert.False(entry.Deleted);
    }

    [Fact]
    public void Converts_a_program_start_from_Japanese_local_time_to_UTC()
    {
        var response = SyoboiResponseParser.ParsePrograms(ProgramsResponse);

        // 2021-03-30 02:10:00 JST is 2021-03-29 17:10:00 UTC.
        var entry = response.Value.Single(program => program.PID == "543959");
        Assert.Equal(new DateTime(2021, 3, 29, 17, 10, 0, DateTimeKind.Utc), entry.StartedAt);
        Assert.Equal(new DateTime(2021, 3, 29, 17, 40, 0, DateTimeKind.Utc), entry.EndedAt);
        Assert.Equal(DateTimeKind.Utc, entry.StartedAt!.Value.Kind);
    }

    [Fact]
    public void A_delayed_slot_keeps_the_delayed_start_and_reports_the_usual_one_separately()
    {
        var response = SyoboiResponseParser.ParsePrograms(ProgramsResponse);

        // StTime already includes StOffset: this slot's run normally starts at
        // 02:10 JST and this airing was pushed back by ten minutes to 02:20,
        // so the offset must not be added to the start time a second time.
        var entry = response.Value.Single(program => program.PID == "538991");
        Assert.Equal(600, entry.StOffset);
        Assert.True(entry.IsDelayed);
        Assert.Equal(new DateTime(2021, 3, 8, 17, 20, 0, DateTimeKind.Utc), entry.StartedAt);
        Assert.Equal(new DateTime(2021, 3, 8, 17, 10, 0, DateTimeKind.Utc), entry.OriginalStartedAt);
    }

    [Fact]
    public void A_slot_that_ran_on_time_has_no_original_start_of_its_own()
    {
        var response = SyoboiResponseParser.ParsePrograms(ProgramsResponse);

        var entry = response.Value.Single(program => program.PID == "543959");
        Assert.Equal(0, entry.StOffset);
        Assert.False(entry.IsDelayed);
        Assert.Null(entry.OriginalStartedAt);
    }

    [Fact]
    public void The_final_episode_flag_is_not_mistaken_for_a_rerun()
    {
        var response = SyoboiResponseParser.ParsePrograms(ProgramsResponse);

        // 0x04 is 終 (final episode); 0x08 is 再 (rerun).
        var entry = response.Value.Single(program => program.PID == "543959");
        Assert.Equal(4, entry.Flag);
        Assert.False(entry.IsRerun);
    }

    [Fact]
    public void Reads_the_deleted_flag()
    {
        var response = SyoboiResponseParser.ParsePrograms(ProgramsResponse);

        Assert.True(response.Value.Single(program => program.PID == "538505").Deleted);
    }

    [Fact]
    public void An_empty_result_is_reported_as_no_data_rather_than_a_failure()
    {
        var response = SyoboiResponseParser.ParsePrograms(NoDataResponse);

        Assert.Equal(SyoboiResponseStatus.NoData, response.Status);
        Assert.False(response.IsError);
        Assert.Equal(404, response.Code);
        Assert.Equal("条件に一致するデータは存在しません", response.Message);
        Assert.Empty(response.Value);
    }

    [Fact]
    public void A_rejected_request_is_reported_as_a_failure_with_its_code_and_message()
    {
        var response = SyoboiResponseParser.ParsePrograms(BadRangeResponse);

        Assert.Equal(SyoboiResponseStatus.Error, response.Status);
        Assert.True(response.IsError);
        Assert.Equal(400, response.Code);
        Assert.Equal("'#Range' が不正です", response.Message);
        Assert.Empty(response.Value);
    }

    [Fact]
    public void A_bare_result_document_is_read_as_the_envelope_it_is()
    {
        var response = SyoboiResponseParser.ParsePrograms(BadCommandResponse);

        Assert.Equal(SyoboiResponseStatus.Error, response.Status);
        Assert.Equal(400, response.Code);
    }

    [Fact]
    public void Returns_an_error_for_malformed_XML()
    {
        var response = SyoboiResponseParser.ParsePrograms("<ProgLookupResponse><ProgItems>");

        Assert.Equal(SyoboiResponseStatus.Error, response.Status);
        Assert.Empty(response.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Returns_an_error_for_a_blank_body(string xml)
    {
        var response = SyoboiResponseParser.ParsePrograms(xml);

        Assert.Equal(SyoboiResponseStatus.Error, response.Status);
        Assert.Empty(response.Value);
    }

    [Fact]
    public void Skips_a_program_entry_missing_its_TID()
    {
        var response = SyoboiResponseParser.ParsePrograms("""
        <ProgLookupResponse><ProgItems><ProgItem id="1"><PID>1</PID><ChID>19</ChID><StTime>2021-03-02 00:00:00</StTime><Count>9</Count></ProgItem></ProgItems></ProgLookupResponse>
        """);

        Assert.Equal(SyoboiResponseStatus.Success, response.Status);
        Assert.Empty(response.Value);
    }

    [Fact]
    public void Falls_back_to_the_element_id_attribute_when_the_PID_field_is_missing()
    {
        var response = SyoboiResponseParser.ParsePrograms("""
        <ProgLookupResponse><ProgItems><ProgItem id="538990"><TID>5877</TID><ChID>70</ChID><StTime>2021-03-02 02:11:00</StTime><Count>9</Count></ProgItem></ProgItems></ProgLookupResponse>
        """);

        Assert.Equal("538990", Assert.Single(response.Value).PID);
    }

    #endregion

    #region Channels

    [Fact]
    public void Parses_channels_keyed_by_ChID()
    {
        var response = SyoboiResponseParser.ParseChannels(ChannelsResponse);

        Assert.Equal(SyoboiResponseStatus.Success, response.Status);
        Assert.Equal(3, response.Value.Count);
        Assert.Equal("TOKYO MX", response.Value[19].ChName);
        Assert.Equal("ＭＸテレビ", response.Value[19].ChiEPGName);
        Assert.Equal(1, response.Value[19].ChGID);
        Assert.Equal(2, response.Value[128].ChGID);
    }

    [Fact]
    public void An_empty_EPG_name_becomes_null_rather_than_an_empty_alias()
    {
        var response = SyoboiResponseParser.ParseChannels(ChannelsResponse);

        Assert.Equal("Netflix", response.Value[256].ChName);
        Assert.Null(response.Value[256].ChiEPGName);
        Assert.Equal(7, response.Value[256].ChGID);
    }

    [Fact]
    public void Parses_channel_groups_keyed_by_ChGID()
    {
        var response = SyoboiResponseParser.ParseChannelGroups(ChannelGroupsResponse);

        Assert.Equal(SyoboiResponseStatus.Success, response.Status);
        Assert.Equal(5, response.Value.Count);
        Assert.Equal("テレビ 関東", response.Value[1].ChGroupName);
        Assert.Equal("インターネット", response.Value[7].ChGroupName);
        Assert.Equal("ラジオ 全国", response.Value[10].ChGroupName);
        Assert.Equal("AbemaTV", response.Value[23].ChGroupName);
    }

    #endregion

    #region Titles

    [Fact]
    public void Parses_titles_keyed_by_TID()
    {
        var response = SyoboiResponseParser.ParseTitles(TitlesResponse);

        Assert.Equal(SyoboiResponseStatus.Success, response.Status);
        var title = Assert.Single(response.Value).Value;
        Assert.Equal(5877, title.TID);
        Assert.Equal("ウマ娘 プリティーダービー Season 2", title.Title);
        Assert.Equal("ウマ娘 プリティーダービー(2)", title.ShortTitle);
        Assert.Equal("うまむすめぷりてぃーだーびーしーずんつー", title.TitleYomi);
        Assert.Null(title.TitleEN);
        Assert.Equal(10, title.Cat);
    }

    [Fact]
    public void An_empty_title_lookup_is_reported_as_no_data()
    {
        var response = SyoboiResponseParser.ParseTitles("""
        <?xml version="1.0" encoding="UTF-8"?><TitleLookupResponse><Result><Code>404</Code><Message>条件に一致するデータは存在しません</Message></Result></TitleLookupResponse>
        """);

        Assert.Equal(SyoboiResponseStatus.NoData, response.Status);
        Assert.Empty(response.Value);
    }

    #endregion
}
