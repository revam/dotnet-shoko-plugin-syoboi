namespace Shoko.Plugin.Syoboi.Models;

/// <summary>
/// A channel from a Syoboi <c>ChLookup</c> response.
/// </summary>
/// <param name="ChID">The Syoboi channel ID.</param>
/// <param name="ChName">The channel's display name.</param>
/// <param name="ChiEPGName">
/// The channel's name as it appears in the Japanese EPG, often full-width
/// (e.g. <c>ＭＸテレビ</c>). Used as an alias when it differs from <paramref name="ChName"/>.
/// </param>
/// <param name="ChGID">The Syoboi channel group ID the channel belongs to.</param>
public sealed record SyoboiChannel(
    int ChID,
    string ChName,
    string? ChiEPGName,
    int ChGID
);
