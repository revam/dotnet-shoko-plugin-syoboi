using System.Collections.Generic;
using System.Text.RegularExpressions;
using Shoko.Abstractions.Metadata;
using Shoko.Abstractions.Metadata.Enums;

namespace Shoko.Plugin.Syoboi.Mapping;

/// <summary>
/// Finds an AniDB anime's Syoboi title ID. AniDB carries it as
/// <c>AniDB_Anime.SyoboiID</c> internally, but plugins only see it through
/// <see cref="Shoko.Abstractions.Metadata.Containers.IWithResources.Resources"/>,
/// as a cross-reference resource pointing at
/// <c>https://cal.syoboi.jp/tid/{id}/time</c>.
/// </summary>
/// <remarks>
/// This is exactly the gap the airing schedule plan calls out under "Plugin
/// sketches / Syoboi Calendar": a typed external ID on <c>IAnidbAnime</c>
/// would avoid parsing a URL, but the abstractions don't have one yet, so
/// this is what's available today. See the follow-up in this plugin's
/// README.
/// </remarks>
public static partial class SyoboiTitleIdResolver
{
    [GeneratedRegex(@"cal\.syoboi\.jp/tid/(?<tid>\d+)(?:/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex TitleIdPattern();

    /// <summary>
    /// Tries to find a Syoboi title ID among the given resources.
    /// </summary>
    /// <param name="resources">The entity's resources, e.g. <c>IAnidbAnime.Resources</c>.</param>
    /// <param name="titleId">The Syoboi title ID, if one was found.</param>
    /// <returns><c>true</c> if a title ID was found.</returns>
    public static bool TryGetTitleId(IReadOnlyList<Resource> resources, out int titleId)
    {
        foreach (var resource in resources)
        {
            if (resource.Type != ResourceType.CrossReference)
                continue;

            var match = TitleIdPattern().Match(resource.Url);
            if (match.Success && int.TryParse(match.Groups["tid"].Value, out titleId))
                return true;
        }

        titleId = 0;
        return false;
    }
}
