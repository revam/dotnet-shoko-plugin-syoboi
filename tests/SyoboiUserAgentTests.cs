using System.Net.Http;
using Shoko.Plugin.Syoboi;
using Shoko.Plugin.Syoboi.Http;
using Xunit;

namespace Shoko.Plugin.Syoboi.Tests;

/// <summary>
/// Both halves of the User-Agent come from user-editable settings, and
/// <see cref="System.Net.Http.Headers.ProductInfoHeaderValue"/> throws a
/// <see cref="FormatException"/> on anything it doesn't like — which, in
/// <c>Plugin.RegisterServices</c>, means the plugin never registers at all.
/// Every case here therefore goes through a real <see cref="HttpClient"/>, so
/// the header parser gets the final say.
/// </summary>
public class SyoboiUserAgentTests
{
    private static string Apply(string? appName, string? url, string? version = null)
    {
        using var client = new HttpClient();
        foreach (var value in SyoboiUserAgent.Build(appName, url, version))
            client.DefaultRequestHeaders.UserAgent.Add(value);

        return client.DefaultRequestHeaders.UserAgent.ToString();
    }

    [Fact]
    public void The_default_configuration_produces_a_usable_header()
    {
        var configuration = new Configuration();

        var userAgent = Apply(configuration.UserAgentAppName, configuration.UserAgentUrl, version: "1.2.3");

        Assert.Equal("Shoko.Plugin.Syoboi/1.2.3 (+https://github.com/ShokoAnime/ShokoServer)", userAgent);
    }

    [Fact]
    public void A_bare_app_name_is_a_product_token_rather_than_a_comment()
    {
        // The bug this guards: "Shoko.Plugin.Syoboi" was passed to the
        // comment constructor, which requires parentheses, so building the
        // client threw and the provider never registered.
        var userAgent = Apply("Shoko.Plugin.Syoboi", url: null, version: "");

        Assert.Equal("Shoko.Plugin.Syoboi", userAgent);
    }

    [Fact]
    public void A_bare_contact_URL_is_wrapped_in_a_comment()
    {
        var userAgent = Apply("MyShoko", "https://example.com/", version: "");

        Assert.Equal("MyShoko (+https://example.com/)", userAgent);
    }

    [Fact]
    public void A_URL_the_user_already_prefixed_is_not_prefixed_twice()
    {
        var userAgent = Apply("MyShoko", "+https://example.com/", version: "");

        Assert.Equal("MyShoko (+https://example.com/)", userAgent);
    }

    [Theory]
    [InlineData("Shoko Syoboi Plugin (dev)", "ShokoSyoboiPlugindev")]
    [InlineData("しょぼいカレンダー", SyoboiUserAgent.DefaultAppName)]
    [InlineData("", SyoboiUserAgent.DefaultAppName)]
    [InlineData(null, SyoboiUserAgent.DefaultAppName)]
    public void An_awkward_app_name_is_reduced_to_a_valid_token(string? appName, string expected)
    {
        var userAgent = Apply(appName, url: null, version: "");

        Assert.Equal(expected, userAgent);
    }

    [Theory]
    [InlineData("https://example.com/a (b)", "MyShoko (+https://example.com/ab)")]
    [InlineData("  https://example.com/  ", "MyShoko (+https://example.com/)")]
    [InlineData("()", "MyShoko")]
    [InlineData("", "MyShoko")]
    [InlineData(null, "MyShoko")]
    public void An_awkward_contact_URL_never_produces_an_invalid_header(string? url, string expected)
    {
        var userAgent = Apply("MyShoko", url, version: "");

        Assert.Equal(expected, userAgent);
    }

    [Fact]
    public void The_version_defaults_to_the_plugin_assembly_version()
    {
        var userAgent = Apply("MyShoko", url: null);

        Assert.StartsWith("MyShoko/", userAgent, StringComparison.Ordinal);
    }
}
