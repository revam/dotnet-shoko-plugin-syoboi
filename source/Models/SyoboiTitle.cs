namespace Shoko.Plugin.Syoboi.Models;

/// <summary>
/// A title from a Syoboi <c>TitleLookup</c> response.
/// </summary>
/// <param name="TID">The Syoboi title ID.</param>
/// <param name="Title">The title's full Japanese name.</param>
/// <param name="ShortTitle">A shortened Japanese name, or <c>null</c> when Syoboi has none.</param>
/// <param name="TitleYomi">The reading of <paramref name="Title"/> in hiragana, or <c>null</c>.</param>
/// <param name="TitleEN">The English name, or <c>null</c>. Rarely filled in.</param>
/// <param name="Cat">
/// Syoboi's category value (10 is a TV anime); see
/// <see href="https://docs.cal.syoboi.jp/spec/title-cat/"/>.
/// </param>
public sealed record SyoboiTitle(
    int TID,
    string Title,
    string? ShortTitle,
    string? TitleYomi,
    string? TitleEN,
    int Cat
);
