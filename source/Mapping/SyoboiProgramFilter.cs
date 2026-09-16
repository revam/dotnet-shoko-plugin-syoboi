using Shoko.Plugin.Syoboi.Models;

namespace Shoko.Plugin.Syoboi.Mapping;

/// <summary>
/// Decides which Syoboi program entries are worth turning into an airing.
/// </summary>
public static class SyoboiProgramFilter
{
    /// <summary>
    /// Whether the entry should be dropped: it was retracted
    /// (<c>Deleted=1</c>), carries no episode number (a marathon block, or
    /// similar), or is flagged as a rerun.
    /// </summary>
    /// <remarks>
    /// The 終 (final episode) flag Syoboi sets on the last slot of a run is
    /// deliberately not a reason to skip: it is still that episode's original
    /// broadcast.
    /// </remarks>
    /// <param name="entry">The entry to check.</param>
    /// <param name="allowMissingEpisodeNumber">
    /// Whether an entry with no episode number is still usable. Syoboi leaves
    /// <c>Count</c> empty on a one-off broadcast — a film or a TV special —
    /// where there is no numbering to state, so it is usable when the anime
    /// it maps onto has exactly one episode to pin it to.
    /// </param>
    /// <returns><c>true</c> if the entry should be skipped.</returns>
    public static bool ShouldSkip(SyoboiProgramEntry entry, bool allowMissingEpisodeNumber = false)
    {
        if (entry.Deleted)
            return true;

        if (entry.Count is not > 0 && !(allowMissingEpisodeNumber && entry.Count is null))
            return true;

        if (entry.IsRerun)
            return true;

        return false;
    }
}
