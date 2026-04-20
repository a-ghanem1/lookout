namespace Lookout.Core;

/// <summary>
/// Filter + pagination descriptor for <see cref="ILookoutStorage"/> queries.
/// All properties are optional; only set ones narrow the result set.
/// </summary>
/// <param name="Type">Entry type discriminator, e.g. <c>http</c>.</param>
/// <param name="Method">HTTP method filter (exact match against the <c>http.method</c> tag).</param>
/// <param name="StatusMin">Inclusive lower bound for <c>http.status</c>. When equal to <see cref="StatusMax"/> acts as an exact match.</param>
/// <param name="StatusMax">Inclusive upper bound for <c>http.status</c>.</param>
/// <param name="Path">Substring match against the <c>http.path</c> tag (case-insensitive).</param>
/// <param name="Q">FTS5 MATCH expression applied to <c>tags_json</c> + <c>content_json</c>.</param>
/// <param name="BeforeUnixMs">Pagination cursor: only entries with <c>timestamp_utc &lt;</c> this value are returned.</param>
/// <param name="Limit">Maximum rows to return. Clamped to <c>[1, 200]</c> by the storage layer.</param>
public sealed record LookoutQuery(
    string? Type = null,
    string? Method = null,
    int? StatusMin = null,
    int? StatusMax = null,
    string? Path = null,
    string? Q = null,
    long? BeforeUnixMs = null,
    int Limit = 50);
