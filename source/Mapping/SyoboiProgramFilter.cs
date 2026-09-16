using Shoko.Plugin.Syoboi.Models;

namespace Shoko.Plugin.Syoboi.Mapping;

/// <summary>
/// Decides which Syoboi program entries are worth turning into an airing.
/// </summary>
public static class SyoboiProgramFilter
{
    /// <summary>
    /// Bit in <see cref="SyoboiProgramEntry.Flag"/> marking a rerun.
    /// </summary>
    public const int RerunFlag = 0x08;

    /// <summary>
    /// Whether the entry should be dropped: it was deleted, carries no
    /// episode number (a marathon block, or similar), or is flagged as a
    /// rerun.
    /// </summary>
    /// <param name="entry">The entry to check.</param>
    /// <returns><c>true</c> if the entry should be skipped.</returns>
    public static bool ShouldSkip(SyoboiProgramEntry entry)
    {
        if (entry.Deleted)
            return true;

        if (entry.Count is not > 0)
            return true;

        if ((entry.Flag & RerunFlag) != 0)
            return true;

        return false;
    }
}
