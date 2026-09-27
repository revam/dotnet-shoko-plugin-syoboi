using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Shoko.Plugin.Syoboi.Mapping;

/// <summary>
/// Reads what Syoboi only states in a slot's free text: the episodes a slot
/// with no <c>Count</c> covers, from its <c>SubTitle</c>, and whether the slot
/// is an advance airing, from its <c>ProgComment</c>.
/// </summary>
public static partial class SyoboiProgramText
{
    /// <summary>
    /// Reads the episode range out of a slot's subtitle. Syoboi leaves
    /// <c>Count</c> empty on a slot airing several episodes back to back, a
    /// double premiere or a one-hour special, and writes the episodes into the
    /// subtitle as <c>#1～#2</c> (also <c>#1〜#2</c>, <c>#1-#2</c> and
    /// <c>#23～24</c>).
    /// </summary>
    /// <param name="subTitle">The slot's <c>SubTitle</c>.</param>
    /// <param name="first">The first episode number of the range.</param>
    /// <param name="last">The last episode number of the range.</param>
    /// <returns>
    /// <c>true</c> when the whole subtitle is a range of two or more episodes;
    /// <c>false</c> for anything else, such as a fractional <c>#14.5</c>, a
    /// range with anything after it, or one that runs backwards.
    /// </returns>
    public static bool TryParseEpisodeRange(string? subTitle, out int first, out int last)
    {
        first = last = 0;
        if (string.IsNullOrWhiteSpace(subTitle))
            return false;

        // NFKC folds the full-width ＃, ～, － and digits into their ASCII
        // forms, leaving the wave dash 〜 for the pattern to accept.
        var match = EpisodeRangeRegex().Match(subTitle.Normalize(NormalizationForm.FormKC).Trim());
        if (!match.Success)
            return false;

        first = int.Parse(match.Groups["first"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture);
        last = int.Parse(match.Groups["last"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture);
        return first > 0 && last > first;
    }

    /// <summary>
    /// Whether a slot's comment marks it as an advance airing (先行放送,
    /// 先行配信, 先に…) rather than a slot of the regular run.
    /// </summary>
    /// <param name="comment">The slot's <c>ProgComment</c>.</param>
    /// <returns><c>true</c> when the comment marks an advance airing.</returns>
    public static bool IsAdvanceAiring(string? comment)
        => !string.IsNullOrWhiteSpace(comment) && AdvanceAiringRegex().IsMatch(comment);

    [GeneratedRegex(@"^#\s*(?<first>[0-9]{1,4})\s*[~〜\-‐–—―ー]\s*#?\s*(?<last>[0-9]{1,4})$", RegexOptions.CultureInvariant)]
    private static partial Regex EpisodeRangeRegex();

    // 先行映像 is preview footage shown inside another programme, not an
    // advance airing of the slot itself.
    [GeneratedRegex("先行(?!映像)|先に", RegexOptions.CultureInvariant)]
    private static partial Regex AdvanceAiringRegex();
}
