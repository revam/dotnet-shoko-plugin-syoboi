namespace Shoko.Plugin.Syoboi.Http;

/// <summary>
/// How a <c>db.php</c> request turned out, as told by the <c>Result/Code</c>
/// envelope in the response body. Every outcome comes back with HTTP 200, so
/// the envelope is the only thing that distinguishes them.
/// </summary>
public enum SyoboiResponseStatus
{
    /// <summary>
    /// Code 200, or no envelope at all — a successful <c>ProgLookup</c> omits
    /// it and answers with <c>ProgItems</c> alone.
    /// </summary>
    Success = 0,

    /// <summary>
    /// Code 404: nothing matched the request. An ordinary, expected answer for
    /// a title with no slots in the requested range, not a failure.
    /// </summary>
    NoData = 1,

    /// <summary>
    /// Any other code (e.g. 400 for a malformed <c>Range</c>), or a body that
    /// isn't well-formed XML.
    /// </summary>
    Error = 2,
}

/// <summary>
/// A parsed <c>db.php</c> response: the envelope's verdict plus whatever rows
/// came with it.
/// </summary>
/// <typeparam name="T">The shape of the parsed rows.</typeparam>
/// <param name="Status">How the request turned out.</param>
/// <param name="Value">The parsed rows. Empty unless <paramref name="Status"/> is <see cref="SyoboiResponseStatus.Success"/>.</param>
/// <param name="Code">The envelope's <c>Result/Code</c>, or <c>200</c> when there was no envelope.</param>
/// <param name="Message">The envelope's <c>Result/Message</c>, if it carried one.</param>
public sealed record SyoboiResponse<T>(
    SyoboiResponseStatus Status,
    T Value,
    int Code = 200,
    string? Message = null
)
{
    /// <summary>
    /// Whether the request failed outright, as opposed to succeeding with no
    /// rows.
    /// </summary>
    public bool IsError => Status is SyoboiResponseStatus.Error;
}
