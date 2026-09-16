using System;

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
/// state one (e.g. a marathon block).
/// </param>
/// <param name="Flag">Bit flags; see <see cref="SyoboiProgramFlags"/>.</param>
/// <param name="Deleted">Whether Syoboi has retracted this entry.</param>
public sealed record SyoboiProgramEntry(
    string PID,
    int TID,
    int ChID,
    DateTime? StartedAt,
    DateTime? EndedAt,
    int StOffset,
    int? Count,
    int Flag,
    bool Deleted
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
}
