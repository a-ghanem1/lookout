namespace Lookout.Core;

/// <summary>A single captured diagnostic entry.</summary>
/// <param name="Id">Unique identifier for this entry.</param>
/// <param name="Type">Entry type discriminator, e.g. <c>http</c>, <c>ef</c>, <c>log</c>.</param>
/// <param name="Timestamp">UTC wall-clock time when the entry was captured.</param>
/// <param name="RequestId">Correlation id from <c>Activity.Current?.RootId</c>; null outside a request context.</param>
/// <param name="DurationMs">Elapsed duration in milliseconds, if applicable.</param>
/// <param name="Tags">Arbitrary key/value metadata attached to this entry.</param>
/// <param name="Content">Raw JSON payload (serialised by the capture point).</param>
public sealed record LookoutEntry(
    Guid Id,
    string Type,
    DateTimeOffset Timestamp,
    string? RequestId,
    double? DurationMs,
    IReadOnlyDictionary<string, string> Tags,
    string Content);
