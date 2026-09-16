using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Shoko.Plugin.Syoboi.Mapping;
using Shoko.Plugin.Syoboi.Models;

namespace Shoko.Plugin.Syoboi.Http;

/// <summary>
/// Parses a Syoboi <c>db.php</c> XML response into typed, ready-to-use
/// records. Kept free of any I/O so it can be unit tested against responses
/// captured from the live service.
/// </summary>
/// <remarks>
/// <para>
/// Every response is XML, whatever the request asks for: the <c>JSON</c> query
/// flag some clients append is silently ignored and the separate
/// <c>json.php</c> endpoint speaks a different, much narrower API.
/// </para>
/// <para>
/// Responses are wrapped in a <c>Result</c> envelope carrying a
/// <c>Code</c>/<c>Message</c> pair, always under HTTP 200 — except a
/// successful <c>ProgLookup</c>, which omits the envelope entirely. Code 404
/// ("条件に一致するデータは存在しません") means nothing matched, which is an
/// ordinary answer rather than a failure.
/// </para>
/// </remarks>
public static class SyoboiResponseParser
{
    private const int SuccessCode = 200;

    private const int NoDataCode = 404;

    /// <summary>
    /// Parses a <c>Command=ProgLookup</c> response.
    /// </summary>
    /// <param name="xml">The raw XML response body.</param>
    /// <param name="logger">Optional. Used to log entries that can't be parsed.</param>
    /// <returns>The program entries, in the order Syoboi listed them.</returns>
    public static SyoboiResponse<IReadOnlyList<SyoboiProgramEntry>> ParsePrograms(string xml, ILogger? logger = null)
    {
        IReadOnlyList<SyoboiProgramEntry> empty = [];
        if (!TryReadEnvelope(xml, empty, logger, out var root, out var failure))
            return failure;

        var programs = new List<SyoboiProgramEntry>();
        foreach (var element in root.Descendants("ProgItem"))
        {
            if (TryMapProgram(element, logger) is { } program)
                programs.Add(program);
        }

        return new SyoboiResponse<IReadOnlyList<SyoboiProgramEntry>>(SyoboiResponseStatus.Success, programs);
    }

    /// <summary>
    /// Parses a <c>Command=ChLookup</c> response.
    /// </summary>
    /// <param name="xml">The raw XML response body.</param>
    /// <param name="logger">Optional. Used to log entries that can't be parsed.</param>
    /// <returns>The channels, keyed by <see cref="SyoboiChannel.ChID"/>.</returns>
    public static SyoboiResponse<IReadOnlyDictionary<int, SyoboiChannel>> ParseChannels(string xml, ILogger? logger = null)
    {
        IReadOnlyDictionary<int, SyoboiChannel> empty = new Dictionary<int, SyoboiChannel>();
        if (!TryReadEnvelope(xml, empty, logger, out var root, out var failure))
            return failure;

        var channels = new Dictionary<int, SyoboiChannel>();
        foreach (var element in root.Descendants("ChItem"))
        {
            if (TryMapChannel(element, logger) is { } channel)
                channels[channel.ChID] = channel;
        }

        return new SyoboiResponse<IReadOnlyDictionary<int, SyoboiChannel>>(SyoboiResponseStatus.Success, channels);
    }

    /// <summary>
    /// Parses a <c>Command=ChGroupLookup</c> response.
    /// </summary>
    /// <param name="xml">The raw XML response body.</param>
    /// <param name="logger">Optional. Used to log entries that can't be parsed.</param>
    /// <returns>The channel groups, keyed by <see cref="SyoboiChannelGroup.ChGID"/>.</returns>
    public static SyoboiResponse<IReadOnlyDictionary<int, SyoboiChannelGroup>> ParseChannelGroups(string xml, ILogger? logger = null)
    {
        IReadOnlyDictionary<int, SyoboiChannelGroup> empty = new Dictionary<int, SyoboiChannelGroup>();
        if (!TryReadEnvelope(xml, empty, logger, out var root, out var failure))
            return failure;

        var groups = new Dictionary<int, SyoboiChannelGroup>();
        foreach (var element in root.Descendants("ChGroupItem"))
        {
            if (TryMapChannelGroup(element, logger) is { } group)
                groups[group.ChGID] = group;
        }

        return new SyoboiResponse<IReadOnlyDictionary<int, SyoboiChannelGroup>>(SyoboiResponseStatus.Success, groups);
    }

    /// <summary>
    /// Parses a <c>Command=TitleLookup</c> response.
    /// </summary>
    /// <param name="xml">The raw XML response body.</param>
    /// <param name="logger">Optional. Used to log entries that can't be parsed.</param>
    /// <returns>The titles, keyed by <see cref="SyoboiTitle.TID"/>.</returns>
    public static SyoboiResponse<IReadOnlyDictionary<int, SyoboiTitle>> ParseTitles(string xml, ILogger? logger = null)
    {
        IReadOnlyDictionary<int, SyoboiTitle> empty = new Dictionary<int, SyoboiTitle>();
        if (!TryReadEnvelope(xml, empty, logger, out var root, out var failure))
            return failure;

        var titles = new Dictionary<int, SyoboiTitle>();
        foreach (var element in root.Descendants("TitleItem"))
        {
            if (TryMapTitle(element, logger) is { } title)
                titles[title.TID] = title;
        }

        return new SyoboiResponse<IReadOnlyDictionary<int, SyoboiTitle>>(SyoboiResponseStatus.Success, titles);
    }

    #region Envelope

    /// <summary>
    /// Reads the document and its <c>Result</c> envelope.
    /// </summary>
    /// <typeparam name="T">The shape of the rows the caller parses.</typeparam>
    /// <param name="xml">The raw XML response body.</param>
    /// <param name="empty">The empty value to answer a failed or empty response with.</param>
    /// <param name="logger">Optional. Used to log why a response was rejected.</param>
    /// <param name="root">The document's root element, when the caller should go on to read rows.</param>
    /// <param name="response">The response to return as-is, when it shouldn't.</param>
    /// <returns><c>true</c> when <paramref name="root"/> is worth reading rows from.</returns>
    private static bool TryReadEnvelope<T>(string xml, T empty, ILogger? logger, out XElement root, out SyoboiResponse<T> response)
    {
        root = default!;
        if (string.IsNullOrWhiteSpace(xml))
        {
            logger?.LogWarning("Syoboi Calendar answered with an empty body.");
            response = new SyoboiResponse<T>(SyoboiResponseStatus.Error, empty, Code: 0, Message: "Empty response body.");
            return false;
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (XmlException ex)
        {
            logger?.LogWarning(ex, "Failed to parse the Syoboi Calendar response as XML.");
            response = new SyoboiResponse<T>(SyoboiResponseStatus.Error, empty, Code: 0, Message: ex.Message);
            return false;
        }

        if (document.Root is not { } documentRoot)
        {
            response = new SyoboiResponse<T>(SyoboiResponseStatus.Error, empty, Code: 0, Message: "Response had no root element.");
            return false;
        }

        // A bad Command answers with a bare <Result> root; everything else
        // wraps it in a per-command <…Response> element, and a successful
        // ProgLookup leaves it out altogether.
        var result = documentRoot.Name.LocalName is "Result" ? documentRoot : documentRoot.Element("Result");
        if (result is null)
        {
            root = documentRoot;
            response = default!;
            return true;
        }

        var message = Trimmed((string?)result.Element("Message"));
        if (!TryParseInt((string?)result.Element("Code"), out var code))
        {
            logger?.LogWarning("Syoboi Calendar answered with an unreadable result code.");
            response = new SyoboiResponse<T>(SyoboiResponseStatus.Error, empty, Code: 0, Message: message);
            return false;
        }

        switch (code)
        {
            case SuccessCode:
                root = documentRoot;
                response = default!;
                return true;

            case NoDataCode:
                logger?.LogDebug("Syoboi Calendar had no data matching the request: {Message}", message);
                response = new SyoboiResponse<T>(SyoboiResponseStatus.NoData, empty, Code: code, Message: message);
                return false;

            default:
                logger?.LogWarning("Syoboi Calendar rejected the request with code {Code}: {Message}", code, message);
                response = new SyoboiResponse<T>(SyoboiResponseStatus.Error, empty, Code: code, Message: message);
                return false;
        }
    }

    #endregion

    #region Rows

    private static SyoboiProgramEntry? TryMapProgram(XElement element, ILogger? logger)
    {
        var pid = FirstNonEmpty((string?)element.Element("PID"), (string?)element.Attribute("id"));
        if (pid is null)
        {
            logger?.LogDebug("Skipping a Syoboi program entry: no PID.");
            return null;
        }

        if (!TryParseInt((string?)element.Element("TID"), out var titleId) || !TryParseInt((string?)element.Element("ChID"), out var channelId))
        {
            logger?.LogDebug("Skipping Syoboi program {PID}: missing or invalid TID/ChID.", pid);
            return null;
        }

        TryParseInt((string?)element.Element("StOffset"), out var offset);
        TryParseInt((string?)element.Element("Flag"), out var flag);
        var count = TryParseInt((string?)element.Element("Count"), out var countValue) && countValue > 0 ? countValue : (int?)null;
        var deleted = TryParseInt((string?)element.Element("Deleted"), out var deletedValue) && deletedValue is not 0;
        var startedAt = SyoboiTimeConverter.ToUtc((string?)element.Element("StTime"));
        var endedAt = SyoboiTimeConverter.ToUtc((string?)element.Element("EdTime"));

        return new SyoboiProgramEntry(pid, titleId, channelId, startedAt, endedAt, offset, count, flag, deleted);
    }

    private static SyoboiChannel? TryMapChannel(XElement element, ILogger? logger)
    {
        if (!TryParseInt(FirstNonEmpty((string?)element.Element("ChID"), (string?)element.Attribute("id")), out var channelId))
        {
            logger?.LogDebug("Skipping a Syoboi channel: missing or invalid ChID.");
            return null;
        }

        if (Trimmed((string?)element.Element("ChName")) is not { } name)
        {
            logger?.LogDebug("Skipping Syoboi channel {ChID}: missing ChName.", channelId);
            return null;
        }

        TryParseInt((string?)element.Element("ChGID"), out var groupId);
        return new SyoboiChannel(channelId, name, Trimmed((string?)element.Element("ChiEPGName")), groupId);
    }

    private static SyoboiChannelGroup? TryMapChannelGroup(XElement element, ILogger? logger)
    {
        if (!TryParseInt(FirstNonEmpty((string?)element.Element("ChGID"), (string?)element.Attribute("id")), out var groupId))
        {
            logger?.LogDebug("Skipping a Syoboi channel group: missing or invalid ChGID.");
            return null;
        }

        if (Trimmed((string?)element.Element("ChGroupName")) is not { } name)
        {
            logger?.LogDebug("Skipping Syoboi channel group {ChGID}: missing ChGroupName.", groupId);
            return null;
        }

        return new SyoboiChannelGroup(groupId, name);
    }

    private static SyoboiTitle? TryMapTitle(XElement element, ILogger? logger)
    {
        if (!TryParseInt(FirstNonEmpty((string?)element.Element("TID"), (string?)element.Attribute("id")), out var titleId))
        {
            logger?.LogDebug("Skipping a Syoboi title: missing or invalid TID.");
            return null;
        }

        if (Trimmed((string?)element.Element("Title")) is not { } title)
        {
            logger?.LogDebug("Skipping Syoboi title {TID}: missing Title.", titleId);
            return null;
        }

        TryParseInt((string?)element.Element("Cat"), out var category);
        return new SyoboiTitle(
            titleId,
            title,
            Trimmed((string?)element.Element("ShortTitle")),
            Trimmed((string?)element.Element("TitleYomi")),
            Trimmed((string?)element.Element("TitleEN")),
            category
        );
    }

    #endregion

    #region Helpers

    private static string? FirstNonEmpty(string? preferred, string? fallback)
        => Trimmed(preferred) ?? Trimmed(fallback);

    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TryParseInt(string? value, out int result)
        => int.TryParse(Trimmed(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    #endregion
}
