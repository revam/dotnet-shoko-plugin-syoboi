using Shoko.Abstractions.Metadata;
using Shoko.Abstractions.Metadata.Enums;
using Shoko.Plugin.Syoboi.Mapping;
using Xunit;

namespace Shoko.Plugin.Syoboi.Tests;

public class SyoboiTitleIdResolverTests
{
    private static Resource MakeResource(string url, ResourceType type = ResourceType.CrossReference, string name = "syoboi")
        => new() { Type = type, Name = name, Url = url };

    [Fact]
    public void Finds_the_title_id_from_the_syoboi_resource()
    {
        var found = SyoboiTitleIdResolver.TryGetTitleId([MakeResource("https://cal.syoboi.jp/tid/6309/time")], out var titleId);

        Assert.True(found);
        Assert.Equal(6309, titleId);
    }

    [Fact]
    public void Ignores_resources_of_other_types()
    {
        var found = SyoboiTitleIdResolver.TryGetTitleId(
            [MakeResource("https://cal.syoboi.jp/tid/6309/time", type: ResourceType.Website)],
            out _);

        Assert.False(found);
    }

    [Fact]
    public void Ignores_unrelated_cross_references()
    {
        var found = SyoboiTitleIdResolver.TryGetTitleId(
            [MakeResource("https://myanimelist.net/anime/12345", name: "mal")],
            out _);

        Assert.False(found);
    }

    [Fact]
    public void Returns_false_for_an_empty_resource_list()
    {
        var found = SyoboiTitleIdResolver.TryGetTitleId([], out var titleId);

        Assert.False(found);
        Assert.Equal(0, titleId);
    }

    [Fact]
    public void Finds_the_title_id_among_several_unrelated_resources()
    {
        Resource[] resources =
        [
            MakeResource("https://myanimelist.net/anime/12345", name: "mal"),
            MakeResource("https://cal.syoboi.jp/tid/6309/time"),
            MakeResource("https://anidb.net/anime/12345", name: "anidb"),
        ];

        var found = SyoboiTitleIdResolver.TryGetTitleId(resources, out var titleId);

        Assert.True(found);
        Assert.Equal(6309, titleId);
    }
}
