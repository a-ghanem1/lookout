namespace Lookout.Core.Schemas;

/// <summary>
/// JSON shape stored in <see cref="LookoutEntry.Content"/> for entries of type <c>http-out</c>.
/// </summary>
/// <param name="Method">HTTP method, e.g. <c>GET</c>.</param>
/// <param name="Url">Full request URL after query-parameter redaction.</param>
/// <param name="StatusCode">HTTP response status code, or <c>null</c> when the request failed before a response was received.</param>
/// <param name="DurationMs">Wall-clock duration from send to first byte of response (or failure), in milliseconds.</param>
/// <param name="RequestHeaders">Outbound request headers (redaction already applied).</param>
/// <param name="ResponseHeaders">Response headers (redaction already applied).</param>
/// <param name="RequestBody">Captured request body text, or <c>null</c> when not captured.</param>
/// <param name="ResponseBody">Captured response body text, or <c>null</c> when not captured.</param>
/// <param name="ErrorType">Full CLR type name of the exception when the send failed; otherwise <c>null</c>.</param>
/// <param name="ErrorMessage">Short exception message when the send failed; otherwise <c>null</c>.</param>
public sealed record OutboundHttpEntryContent(
    string Method,
    string Url,
    int? StatusCode,
    double DurationMs,
    IReadOnlyDictionary<string, string> RequestHeaders,
    IReadOnlyDictionary<string, string> ResponseHeaders,
    string? RequestBody,
    string? ResponseBody,
    string? ErrorType,
    string? ErrorMessage);
