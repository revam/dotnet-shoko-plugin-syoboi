using System.Net;
using System.Net.Http;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Shoko.Abstractions.Config;
using Shoko.Abstractions.Config.Services;
using Shoko.Abstractions.Metadata;
using Shoko.Abstractions.Metadata.Anidb;
using Shoko.Abstractions.Metadata.Enums;
using Shoko.Abstractions.Metadata.Services;

namespace Shoko.Plugin.Syoboi.Tests;

/// <summary>
/// Builds a stand-in for one of the host's interfaces, answering the members
/// a test names and throwing <see cref="NotSupportedException"/> for every
/// other one, so a code path that starts asking the host for more announces
/// itself instead of quietly passing. The host's interfaces are wide and
/// still moving, so this beats writing out a hundred members that only throw.
/// </summary>
internal static class Stub
{
    /// <summary>
    /// A stand-in for <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The interface to stand in for.</typeparam>
    /// <param name="answers">
    /// The members to answer, by name, each taking the call's arguments.
    /// Property getters are named without their <c>get_</c> prefix, and events
    /// are always accepted and never raised.
    /// </param>
    /// <returns>The stand-in.</returns>
    public static T Of<T>(params (string Member, Func<object?[], object?> Answer)[] answers) where T : class
        => StubProxy<T>.Create(answers.ToDictionary(answer => answer.Member, answer => answer.Answer, StringComparer.Ordinal));
}

/// <inheritdoc cref="Stub"/>
/// <typeparam name="T">The interface to stand in for.</typeparam>
internal class StubProxy<T> : DispatchProxy where T : class
{
    private IReadOnlyDictionary<string, Func<object?[], object?>> _answers = new Dictionary<string, Func<object?[], object?>>(StringComparer.Ordinal);

    internal static T Create(IReadOnlyDictionary<string, Func<object?[], object?>> answers)
    {
        var proxy = Create<T, StubProxy<T>>()!;
        ((StubProxy<T>)(object)proxy)._answers = answers;
        return proxy;
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">The member was not stubbed.</exception>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var name = targetMethod!.Name;
        if (name.StartsWith("add_", StringComparison.Ordinal) || name.StartsWith("remove_", StringComparison.Ordinal))
            return null;

        var member = name.StartsWith("get_", StringComparison.Ordinal) || name.StartsWith("set_", StringComparison.Ordinal) ? name[4..] : name;
        return _answers.TryGetValue(member, out var answer)
            ? answer(args ?? [])
            : throw new NotSupportedException($"{typeof(T).Name}.{member} was not stubbed.");
    }
}

/// <summary>
/// The host services the sweep tests stand up.
/// </summary>
internal static class Host
{
    /// <summary>
    /// A configuration provider handing back one configuration instance.
    /// </summary>
    /// <param name="configuration">The configuration to hand back.</param>
    /// <returns>The provider.</returns>
    public static ConfigurationProvider<Configuration> ConfigurationProvider(Configuration configuration)
        => new(Stub.Of<IConfigurationService>(
            // The provider routes Load() through the configuration's info, and
            // the info is only compared for identity on a Saved event nothing
            // here raises, so it need not be a real one.
            ("GetConfigurationInfo", _ => null),
            ("Load", _ => configuration)
        ));

    /// <summary>
    /// A metadata service whose AniDB provider holds the given anime.
    /// </summary>
    /// <param name="anime">The anime the sweep walks.</param>
    /// <returns>The metadata service.</returns>
    public static IMetadataService MetadataService(IEnumerable<IAnidbAnime> anime)
        => Stub.Of<IMetadataService>(("GetAllSeriesForProvider", args =>
            (IMetadataService.ProviderName)args[0]! is IMetadataService.ProviderName.AniDB ? anime.Cast<ISeries>() : Enumerable.Empty<ISeries>()));

    /// <summary>
    /// An AniDB anime carrying a Syoboi title ID as a cross-reference
    /// resource, and no episodes for an airing to be pinned to.
    /// </summary>
    /// <param name="animeId">The AniDB anime ID.</param>
    /// <param name="syoboiTitleId">The Syoboi title ID to carry, if any.</param>
    /// <returns>The anime.</returns>
    public static IAnidbAnime AnidbAnime(int animeId, int? syoboiTitleId = null)
        => Stub.Of<IAnidbAnime>(
            ("ID", _ => animeId),
            ("Resources", _ => syoboiTitleId is { } titleId
                ? new[] { new Resource { Type = ResourceType.CrossReference, Name = "Syoboi Calendar", Url = $"https://cal.syoboi.jp/tid/{titleId}/time" } }
                : Array.Empty<Resource>()),
            ("AirDate", _ => null),
            ("EndDate", _ => null),
            ("Episodes", _ => Array.Empty<IAnidbEpisode>())
        );
}

/// <summary>
/// An <see cref="ILogger{TCategoryName}"/> that records the level of each
/// entry, so a test can assert how noisy a code path is.
/// </summary>
/// <typeparam name="T">The category the logger is for.</typeparam>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<LogLevel> Entries { get; } = [];

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Add(logLevel);
}

/// <summary>
/// An <see cref="HttpMessageHandler"/> answering cal.syoboi.jp's three
/// commands with captured XML, recording every request, and able to abort one
/// the way a sweep's deadline aborts the request in flight.
/// </summary>
/// <param name="abortWhen">
/// Optional. Called with each request URI and everything requested so far;
/// answering <c>true</c> aborts that request.
/// </param>
internal sealed class StubSyoboiHandler(Func<string, IReadOnlyList<string>, bool>? abortWhen = null) : HttpMessageHandler
{
    private readonly List<string> _requests = [];

    /// <summary>
    /// Every request URI, in order.
    /// </summary>
    public IReadOnlyList<string> Requests => _requests;

    /// <inheritdoc/>
    /// <exception cref="OperationCanceledException">The request was aborted.</exception>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!.PathAndQuery;
        _requests.Add(uri);

        if (abortWhen is not null && abortWhen(uri, _requests))
            throw new OperationCanceledException(cancellationToken);

        var body = uri.Contains("Command=ChGroupLookup", StringComparison.Ordinal) ? SyoboiXml.ChannelGroups
            : uri.Contains("Command=ChLookup", StringComparison.Ordinal) ? SyoboiXml.Channels
            : SyoboiXml.NoPrograms;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(body, System.Text.Encoding.UTF8, "text/xml"),
        });
    }
}

/// <summary>
/// The captured cal.syoboi.jp responses the sweep tests are driven by, trimmed
/// to the rows they need.
/// </summary>
internal static class SyoboiXml
{
    /// <summary>
    /// <c>Command=ChLookup</c>.
    /// </summary>
    public const string Channels = """
    <?xml version="1.0" encoding="UTF-8"?><ChLookupResponse><Result><Code>200</Code><Message></Message></Result><ChItems><ChItem id="19"><ChID>19</ChID><ChName>TOKYO MX</ChName><ChiEPGName>ＭＸテレビ</ChiEPGName><ChGID>1</ChGID></ChItem></ChItems></ChLookupResponse>
    """;

    /// <summary>
    /// <c>Command=ChGroupLookup</c>.
    /// </summary>
    public const string ChannelGroups = """
    <?xml version="1.0" encoding="UTF-8"?><ChGroupLookupResponse><Result><Code>200</Code><Message></Message></Result><ChGroupItems><ChGroupItem id="1"><ChGID>1</ChGID><ChGroupName>テレビ 関東</ChGroupName></ChGroupItem></ChGroupItems></ChGroupLookupResponse>
    """;

    /// <summary>
    /// <c>Command=ProgLookup</c>, for a window with no broadcast slots in it.
    /// </summary>
    public const string NoPrograms = """
    <?xml version="1.0" encoding="UTF-8"?><ProgLookupResponse><Result><Code>404</Code><Message>条件に一致するデータは存在しません</Message></Result></ProgLookupResponse>
    """;
}
