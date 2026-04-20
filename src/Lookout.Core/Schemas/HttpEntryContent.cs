namespace Lookout.Core.Schemas;

/// <summary>
/// JSON shape stored in <see cref="LookoutEntry.Content"/> for entries of type <c>http</c>.
/// </summary>
/// <param name="Method">HTTP method, e.g. <c>GET</c>.</param>
/// <param name="Path">Request path without the query string.</param>
/// <param name="QueryString">Raw query string including the leading <c>?</c>, or empty.</param>
/// <param name="StatusCode">Response status code.</param>
/// <param name="DurationMs">Wall-clock request duration in milliseconds.</param>
/// <param name="RequestHeaders">Request headers (redaction already applied).</param>
/// <param name="ResponseHeaders">Response headers (redaction already applied).</param>
/// <param name="RequestBody">Captured request body text, or <c>null</c> when not captured.</param>
/// <param name="ResponseBody">Captured response body text, or <c>null</c> when not captured.</param>
/// <param name="User">Authenticated user name, or <c>null</c> for anonymous requests.</param>
public sealed record HttpEntryContent(
    string Method,
    string Path,
    string QueryString,
    int StatusCode,
    double DurationMs,
    IReadOnlyDictionary<string, string> RequestHeaders,
    IReadOnlyDictionary<string, string> ResponseHeaders,
    string? RequestBody,
    string? ResponseBody,
    string? User);
