using System;
using System.Text;
using Shoko.Abstractions.Metadata.Airing;

namespace Shoko.Plugin.Syoboi.Mapping;

/// <summary>
/// Classifies a Syoboi channel group name (<c>ChGroupLookup</c>'s
/// <c>ChGroupName</c>) as television, streaming, or radio (which is dropped
/// entirely — <c>AiringChannelType</c> has no kind for audio-only
/// broadcasts).
/// </summary>
/// <remarks>
/// Syoboi's groups are regional or carrier-based rather than typed: <c>テレビ
/// 関東</c>, <c>テレビ 近畿</c>, … for terrestrial stations, <c>BSデジタル</c>,
/// <c>BSデジタル4K/8K</c> and <c>スカパー</c> for satellite, <c>インターネット</c>
/// and <c>AbemaTV</c> for streaming, <c>ラジオ 全国</c>, <c>ラジオ 関東</c>, …
/// for radio, and <c>その他</c>/<c>信越放送</c> for the rest. Matching on
/// markers rather than an exhaustive list keeps a newly added group working.
/// </remarks>
public static class SyoboiChannelGroupClassifier
{
    // 配信 ("distribution") covers ネット配信 and friends; Abema and ニコニコ are
    // named groups of their own rather than being filed under インターネット.
    private static readonly string[] _streamingMarkers = ["インターネット", "配信", "ネット", "streaming", "abema", "ニコニコ", "niconico"];

    private static readonly string[] _radioMarkers = ["ラジオ", "radio"];

    /// <summary>
    /// Classifies a channel group by name.
    /// </summary>
    /// <param name="channelGroupName">The group's <c>ChGroupName</c>.</param>
    /// <returns>
    /// The channel type to register matching channels under, or <c>null</c>
    /// when the group is radio and should be skipped entirely.
    /// </returns>
    public static AiringChannelType? Classify(string channelGroupName)
    {
        if (string.IsNullOrWhiteSpace(channelGroupName))
            return AiringChannelType.Television;

        var normalized = channelGroupName.Normalize(NormalizationForm.FormKC);

        // Checked first: インターネットラジオ is radio, not streaming.
        foreach (var marker in _radioMarkers)
        {
            if (Contains(normalized, marker))
                return null;
        }

        foreach (var marker in _streamingMarkers)
        {
            if (Contains(normalized, marker))
                return AiringChannelType.Streaming;
        }

        // テレビ 関東, BSデジタル, スカパー, 信越放送, その他 and anything new all
        // land here, which covers the overwhelming majority of the groups.
        return AiringChannelType.Television;
    }

    private static bool Contains(string haystack, string needle)
        => haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
