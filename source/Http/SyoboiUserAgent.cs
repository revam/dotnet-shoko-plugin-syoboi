using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;

namespace Shoko.Plugin.Syoboi.Http;

/// <summary>
/// Builds the <c>User-Agent</c> header cal.syoboi.jp asks clients to identify
/// themselves with: <c>AppName/version (+https://example.com/)</c>. Clients
/// that send a default or missing User-Agent are throttled to one request per
/// ten seconds instead of one per second.
/// </summary>
/// <remarks>
/// Both halves come from user-editable settings, so both are normalised into
/// something the HTTP header parser accepts rather than handed to
/// <see cref="ProductInfoHeaderValue"/> as-is: its single-argument constructor
/// takes a <em>comment</em>, which must be wrapped in parentheses, and its
/// two-argument constructor takes a product name, which must be a bare HTTP
/// token. Feeding either the wrong shape throws a
/// <see cref="FormatException"/>, and doing that here would leave the whole
/// plugin unable to start.
/// </remarks>
public static class SyoboiUserAgent
{
    /// <summary>
    /// The product name used when the configured one is empty or has no
    /// usable characters.
    /// </summary>
    public const string DefaultAppName = "Shoko.Plugin.Syoboi";

    /// <summary>
    /// Builds the User-Agent header values for the given settings.
    /// </summary>
    /// <param name="appName">The configured application name. Anything that isn't a valid HTTP token character is dropped.</param>
    /// <param name="url">Optional. The configured contact URL, added as a <c>(+url)</c> comment when it has any usable characters.</param>
    /// <param name="version">
    /// Optional. The product version. Defaults to this assembly's version;
    /// pass an empty string for no version at all.
    /// </param>
    /// <returns>The header values to add, in order. Never empty.</returns>
    public static IReadOnlyList<ProductInfoHeaderValue> Build(string? appName, string? url, string? version = null)
    {
        var product = SanitizeToken(appName) ?? DefaultAppName;
        var productVersion = SanitizeToken(version ?? GetAssemblyVersion());
        var values = new List<ProductInfoHeaderValue>(2) { new(product, productVersion) };

        if (SanitizeComment(url) is { } comment)
            values.Add(new ProductInfoHeaderValue(comment));

        return values;
    }

    /// <summary>
    /// Keeps only the characters that are safe in an HTTP token, so the
    /// result is always a valid product name or version.
    /// </summary>
    /// <param name="value">The value to sanitize.</param>
    /// <returns>The sanitized token, or <c>null</c> if nothing usable was left.</returns>
    private static string? SanitizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or '+' or '~')
                builder.Append(character);
        }

        return builder.Length > 0 ? builder.ToString() : null;
    }

    /// <summary>
    /// Wraps a contact URL in the parentheses a User-Agent comment needs,
    /// dropping the characters that would make the header invalid (its own
    /// parentheses, backslashes and control characters).
    /// </summary>
    /// <param name="url">The URL to wrap.</param>
    /// <returns>The comment, or <c>null</c> if nothing usable was left.</returns>
    private static string? SanitizeComment(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var builder = new StringBuilder(url.Length + 3);
        foreach (var character in url.Trim())
        {
            if (character is '(' or ')' or '\\' || char.IsControl(character) || char.IsWhiteSpace(character))
                continue;

            builder.Append(character);
        }

        if (builder.Length is 0)
            return null;

        builder.Insert(0, builder[0] is '+' ? "(" : "(+").Append(')');
        return builder.ToString();
    }

    private static string? GetAssemblyVersion()
        => typeof(SyoboiUserAgent).Assembly.GetName().Version?.ToString(3);
}
