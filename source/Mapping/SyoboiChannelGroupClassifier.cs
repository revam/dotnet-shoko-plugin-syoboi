using System;
using System.Text;
using Shoko.Abstractions.Metadata.Airing;

namespace Shoko.Plugin.Syoboi.Mapping;

/// <summary>
/// Classifies a Syoboi channel group name (<c>ChGroupLookup</c>'s
/// <c>ChGName</c>) as television, streaming, or radio (which is dropped
/// entirely — <c>AiringScheduleData.Tracks</c> has no kind for audio-only
/// broadcasts).
/// </summary>
public static class SyoboiChannelGroupClassifier
{
    /// <summary>
    /// Classifies a channel group by name.
    /// </summary>
    /// <param name="channelGroupName">The group's <c>ChGName</c>.</param>
    /// <returns>
    /// The channel type to register matching channels under, or <c>null</c>
    /// when the group is radio and should be skipped entirely.
    /// </returns>
    public static AiringChannelType? Classify(string channelGroupName)
    {
        if (string.IsNullOrWhiteSpace(channelGroupName))
            return AiringChannelType.Television;

        var normalized = channelGroupName.Normalize(NormalizationForm.FormKC);

        if (Contains(normalized, "ラジオ") || Contains(normalized, "radio"))
            return null;

        if (Contains(normalized, "ネット") || Contains(normalized, "配信") || Contains(normalized, "streaming") || Contains(normalized, "net"))
            return AiringChannelType.Streaming;

        // TV, BS/CS and anything else defaults to television, which covers
        // the overwhelming majority of Syoboi's own channel groups.
        return AiringChannelType.Television;
    }

    private static bool Contains(string haystack, string needle)
        => haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
