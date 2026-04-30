namespace Lookout.Core;

/// <summary>
/// Filter + pagination descriptor for <see cref="ILookoutStorage"/> queries.
/// All properties are optional; only set ones narrow the result set.
/// </summary>
/// <param name="Type">Entry type discriminator, e.g. <c>http</c>. Use <see cref="TypeIn"/> for multi-type filtering.</param>
/// <param name="TypeIn">Multi-type filter. When non-empty, overrides <see cref="Type"/> and generates an IN clause.</param>
/// <param name="Method">HTTP method filter (exact match against the <c>http.method</c> tag).</param>
/// <param name="StatusMin">Inclusive lower bound for <c>http.status</c>. When equal to <see cref="StatusMax"/> acts as an exact match.</param>
/// <param name="StatusMax">Inclusive upper bound for <c>http.status</c>.</param>
/// <param name="Path">Substring match against the <c>http.path</c> tag (case-insensitive).</param>
/// <param name="Q">FTS5 MATCH expression applied to <c>tags_json</c> + <c>content_json</c>.</param>
/// <param name="BeforeUnixMs">Pagination cursor: only entries with <c>timestamp_utc &lt;</c> this value are returned.</param>
/// <param name="Limit">Maximum rows to return. Clamped to <c>[1, 200]</c> by the storage layer.</param>
/// <param name="Sort">Sort order: <c>"duration"</c> sorts by <c>duration_ms DESC</c>; any other value (or null) sorts by <c>timestamp_utc DESC</c>.</param>
/// <param name="MinDurationMs">Inclusive lower bound for <c>duration_ms</c>.</param>
/// <param name="MaxDurationMs">Inclusive upper bound for <c>duration_ms</c>.</param>
/// <param name="UrlHost">Substring match against the <c>http.url.host</c> tag (case-insensitive).</param>
/// <param name="ErrorsOnly">When true, restricts to entries that have an <c>http.error</c> tag (network-level failures).</param>
/// <param name="MinLevel">Inclusive minimum log level for <c>log.level</c> tag (Trace/Debug/Information/Warning/Error/Critical).</param>
/// <param name="Handled">When set, filters exceptions by <c>exception.handled</c> tag.</param>
/// <param name="Tags">Tag key=value pairs that must ALL be present (AND semantics). Each tuple is (key, value).</param>
public sealed record LookoutQuery(
    string? Type = null,
    IReadOnlyList<string>? TypeIn = null,
    string? Method = null,
    int? StatusMin = null,
    int? StatusMax = null,
    string? Path = null,
    string? Q = null,
    long? BeforeUnixMs = null,
    int Limit = 50,
    string? Sort = null,
    double? MinDurationMs = null,
    double? MaxDurationMs = null,
    string? UrlHost = null,
    bool? ErrorsOnly = null,
    string? MinLevel = null,
    bool? Handled = null,
    IReadOnlyList<(string Key, string Value)>? Tags = null);
