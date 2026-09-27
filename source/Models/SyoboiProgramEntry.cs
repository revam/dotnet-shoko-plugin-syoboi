using System;
using Shoko.Plugin.Syoboi.Mapping;

namespace Shoko.Plugin.Syoboi.Models;

/// <summary>
/// One broadcast slot from a Syoboi <c>ProgLookup</c> response (a "ProgItem").
/// </summary>
/// <param name="PID">The program's own ID. Stable across requests.</param>
/// <param name="TID">The Syoboi title ID the program belongs to.</param>
/// <param name="ChID">The Syoboi channel ID the program airs on.</param>
/// <param name="StartedAt">
/// The slot's start, in UTC, or <c>null</c> when Syoboi's <c>StTime</c> could
/// not be parsed. Syoboi gives it as Japanese local time with no zone marker,
/// and it already accounts for <paramref name="StOffset"/>.
/// </param>
/// <param name="EndedAt">The slot's end, in UTC, in the same shape as <paramref name="StartedAt"/>.</param>
/// <param name="StOffset">
/// How many seconds the slot was pushed back from the run's usual time, as
/// Syoboi's <c>StOffset</c>. This is recorded after the fact: a slot delayed
/// by ten minutes has both an <c>StOffset</c> of <c>600</c> and an
/// <c>StTime</c> ten minutes past the usual one, so the offset must not be
/// added to the start time again — see <see cref="OriginalStartedAt"/>.
/// </param>
/// <param name="Count">
/// The episode number this slot is for, or <c>null</c> when Syoboi doesn't
/// state one: a film, a special, or several episodes back to back, which
/// <paramref name="SubTitle"/> then names.
/// </param>
/// <param name="Flag">Bit flags; see <see cref="SyoboiProgramFlags"/>.</param>
/// <param name="Deleted">Whether Syoboi has retracted this entry.</param>
/// <param name="SubTitle">
/// Optional. The slot's subtitle, which holds the episode range
/// (<c>#1～#2</c>) of a slot airing several episodes at once.
/// </param>
/// <param name="Comment">
/// Optional. The slot's <c>ProgComment</c>, which marks an advance airing.
/// </param>
public sealed record SyoboiProgramEntry(
    string PID,
    int TID,
    int ChID,
    DateTime? StartedAt,
    DateTime? EndedAt,
    int StOffset,
    int? Count,
    int Flag,
    bool Deleted,
    string? SubTitle = null,
    string? Comment = null
)
{
    /// <summary>
    /// The slot the run normally occupies, in UTC — that is,
    /// <see cref="StartedAt"/> with the delay Syoboi recorded in
    /// <see cref="StOffset"/> taken back off. <c>null</c> when the slot ran on
    /// time, or when the start time could not be parsed.
    /// </summary>
    public DateTime? OriginalStartedAt
        => StOffset is not 0 && StartedAt is { } startedAt ? startedAt.AddSeconds(-StOffset) : null;

    /// <summary>
    /// Whether the slot was pushed back from the run's usual time.
    /// </summary>
    public bool IsDelayed => StOffset > 0;

    /// <summary>
    /// Whether Syoboi marks this slot as a rerun.
    /// </summary>
    public bool IsRerun => (Flag & (int)SyoboiProgramFlags.Rerun) is not 0;

    /// <summary>
    /// The episodes a slot with no <see cref="Count"/> covers, read from its
    /// <see cref="SubTitle"/>, or <c>null</c> when it has a count or names no
    /// range.
    /// </summary>
    public (int First, int Last)? EpisodeRange
        => Count is null && SyoboiProgramText.TryParseEpisodeRange(SubTitle, out var first, out var last) ? (first, last) : null;

    /// <summary>
    /// Whether the comment marks the slot as an advance airing (先行放送,
    /// 先行配信, …) rather than a slot of the regular run.
    /// </summary>
    public bool IsAdvance => SyoboiProgramText.IsAdvanceAiring(Comment);
}
