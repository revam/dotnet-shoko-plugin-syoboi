namespace Shoko.Plugin.Syoboi.Models;

/// <summary>
/// A channel group from a Syoboi <c>ChGroupLookup</c> response, used to tell
/// regional television, satellite, internet streaming and radio channels
/// apart.
/// </summary>
/// <param name="ChGID">The Syoboi channel group ID.</param>
/// <param name="ChGroupName">
/// The group's display name, e.g. <c>テレビ 関東</c>, <c>BSデジタル</c>,
/// <c>インターネット</c>, <c>AbemaTV</c> or <c>ラジオ 全国</c>.
/// </param>
public sealed record SyoboiChannelGroup(
    int ChGID,
    string ChGroupName
);
