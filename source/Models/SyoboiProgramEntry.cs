namespace Shoko.Plugin.Syoboi.Models;

/// <summary>
/// One broadcast slot from a Syoboi <c>ProgLookup</c> response (a "ProgItem").
/// </summary>
/// <param name="PID">The program's own ID. Stable across requests.</param>
/// <param name="TID">The Syoboi title ID the program belongs to.</param>
/// <param name="ChID">The Syoboi channel ID the program airs on.</param>
/// <param name="StTime">
/// The scheduled start time, as Syoboi gives it: <c>yyyyMMddHHmmss</c> in
/// Japan Standard Time, with no zone marker.
/// </param>
/// <param name="StOffset">
/// Extra seconds added to <paramref name="StTime"/> to get the actual start.
/// </param>
/// <param name="EdTime">The scheduled end time, in the same shape as <paramref name="StTime"/>.</param>
/// <param name="Count">
/// The episode number this slot is for, or <c>null</c> when Syoboi doesn't
/// state one (e.g. a marathon block).
/// </param>
/// <param name="Flag">
/// Bit flags. Bit <c>0x08</c> marks a rerun.
/// </param>
/// <param name="Deleted">Whether Syoboi has retracted this entry.</param>
public sealed record SyoboiProgramEntry(
    string PID,
    int TID,
    int ChID,
    string? StTime,
    int StOffset,
    string? EdTime,
    int? Count,
    int Flag,
    bool Deleted
);
