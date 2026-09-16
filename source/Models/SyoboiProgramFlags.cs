using System;

namespace Shoko.Plugin.Syoboi.Models;

/// <summary>
/// The bits Syoboi sets in a <c>ProgItem</c>'s <c>Flag</c> field, as
/// documented under
/// <see href="https://docs.cal.syoboi.jp/spec/proginfo-flag/">放送時間データのフラグ値一覧</see>.
/// The site renders each as a single-character badge next to the slot.
/// </summary>
[Flags]
public enum SyoboiProgramFlags
{
    /// <summary>
    /// No badge: an ordinary slot.
    /// </summary>
    None = 0,

    /// <summary>
    /// 注 — the slot carries a notice (a schedule change, a special
    /// broadcast, and so on).
    /// </summary>
    Notice = 0x01,

    /// <summary>
    /// 新 — the first episode of a run.
    /// </summary>
    FirstEpisode = 0x02,

    /// <summary>
    /// 終 — the final episode of a run. Not a reason to skip the slot: it is
    /// still the episode's original broadcast.
    /// </summary>
    FinalEpisode = 0x04,

    /// <summary>
    /// 再 — a rerun.
    /// </summary>
    Rerun = 0x08,
}
