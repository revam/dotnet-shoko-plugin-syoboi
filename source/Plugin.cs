using System;
using System.Net.Http.Headers;
using System.Threading;
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
    /// <summary>
    /// The embedded resource of the plugin's thumbnail.
    /// </summary>
    internal const string ThumbnailResourceName = "Shoko.Plugin.Syoboi.Assets.thumbnail.svg";

    /// <summary>
    /// The embedded resource of the plugin's icon.
    /// </summary>
    internal const string IconResourceName = "Shoko.Plugin.Syoboi.Assets.icon.svg";

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
    public string? EmbeddedThumbnailResourceName => ThumbnailResourceName;

    /// <inheritdoc/>
    public string? EmbeddedIconResourceName => IconResourceName;

    /// <inheritdoc/>
    public static void RegisterServices(IServiceCollection serviceCollection, IApplicationPaths applicationPaths)
    {
        // The provider itself is not registered: the server finds it by
        // reflection, constructs it with these services, and sweeps that very
        // instance, so nothing here needs a reference to it.
        serviceCollection.AddSingleton<SyoboiRateLimiter>();
        serviceCollection.AddSingleton<SyoboiChannelDirectoryCache>();

        serviceCollection
            // Syoboi asks for a custom User-Agent in the form "AppName (+url)";
            // clients without one are throttled harder (see SyoboiConstants).
            // The contact URL is read from the plugin's registered info rather
            // than written here, and left off when a local build has none.
            .AddHttpClient<SyoboiApiClient>((provider, client) =>
            {
                var info = provider.GetRequiredService<IPluginManager>().GetPluginInfo<Plugin>();
                client.BaseAddress = new Uri(SyoboiConstants.BaseUrl);
                client.DefaultRequestHeaders.UserAgent.Add(
                    new ProductInfoHeaderValue("Shoko.Plugin.Syoboi", info?.Version.Version.ToString(3) ?? typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "1.0.0")
                );
                if (info?.RepositoryUrl is { Length: > 0 } repositoryUrl)
                    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue($"(+{repositoryUrl})"));
            })
            // The provider holds on to its client for the life of the server,
            // so one handler is kept and its pooled connections recycled.
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .UseSocketsHttpHandler((handler, _) =>
            {
                handler.PooledConnectionLifetime = TimeSpan.FromMinutes(5);
                handler.PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2);
            });
    }
}
