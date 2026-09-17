using System;
using Microsoft.Extensions.DependencyInjection;
using Shoko.Abstractions.Config;
using Shoko.Abstractions.Plugin;
using Shoko.Plugin.Syoboi.Http;

namespace Shoko.Plugin.Syoboi;

/// <summary>
/// Plugin providing airing schedules for Japanese TV and streaming broadcasts,
/// sourced from cal.syoboi.jp (Syoboi Calendar).
/// </summary>
public class Plugin : IPlugin, IPluginServiceRegistration
{
    /// <inheritdoc/>
    public Guid ID { get; private init; } = new("2f4b7a3e-9c1d-4f6a-8b2e-5d3c7a1f9e0b");

    /// <inheritdoc/>
    public string Name { get; private set; } = "Syoboi Calendar";

    /// <inheritdoc/>
    public string Description { get; private set; } = """
        Provides Japanese TV and streaming broadcast schedules from cal.syoboi.jp
        (Syoboi Calendar), keyed to AniDB anime through their Syoboi title ID.
    """;

    /// <inheritdoc/>
    public static void RegisterServices(IServiceCollection serviceCollection, IApplicationPaths applicationPaths)
    {
        // The provider itself is not registered: the server finds it by
        // reflection, constructs it with these services, and sweeps that very
        // instance, so nothing here needs a reference to it.
        serviceCollection.AddSingleton<SyoboiRateLimiter>();
        serviceCollection.AddSingleton<SyoboiChannelDirectoryCache>();

        serviceCollection.AddHttpClient<SyoboiApiClient>((sp, client) =>
        {
            var configProvider = sp.GetRequiredService<ConfigurationProvider<Configuration>>();
            var config = configProvider.Load();
            client.BaseAddress = new Uri(SyoboiConstants.BaseUrl);
            client.DefaultRequestHeaders.UserAgent.Clear();
            // Syoboi asks for a custom User-Agent in the form "AppName (+url)";
            // clients without one are throttled harder (see SyoboiConstants).
            // Both halves come from user-editable settings, so SyoboiUserAgent
            // normalises them into something the header parser accepts.
            foreach (var value in SyoboiUserAgent.Build(config.UserAgentAppName, config.UserAgentUrl))
                client.DefaultRequestHeaders.UserAgent.Add(value);
        });
    }
}
