namespace Shoko.Plugin.Syoboi.Models;

/// <summary>
/// A channel group from a Syoboi <c>ChGroupLookup</c> response, used to tell
/// TV, BS/CS, internet streaming and radio channels apart.
/// </summary>
/// <param name="ChGID">The Syoboi channel group ID.</param>
/// <param name="ChGName">The group's display name.</param>
public sealed record SyoboiChannelGroup(
    int ChGID,
    string ChGName
);
