namespace Lookout.Core;

/// <summary>Options for configuring Lookout.</summary>
public sealed class LookoutOptions
{
    /// <summary>
    /// Environments in which Lookout is permitted to run. Default: ["Development"].
    /// </summary>
    public IList<string> AllowInEnvironments { get; set; } = ["Development"];

    /// <summary>
    /// When true, Lookout is allowed to run in any environment, including Production.
    /// Intended as an explicit opt-in escape hatch — logs a startup warning when used.
    /// </summary>
    public bool AllowInProduction { get; set; }

    /// <summary>
    /// Path to the SQLite database file used for entry storage.
    /// Default: <c>%LocalAppData%/Lookout/lookout.db</c> (cross-platform equivalent).
    /// </summary>
    public string StoragePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lookout",
        "lookout.db");

    /// <summary>
    /// Retention window in hours. Entries older than this are pruned. Default: 24.
    /// </summary>
    public int MaxAgeHours { get; set; } = 24;

    /// <summary>
    /// Maximum number of entries retained as a secondary safety cap. Default: 50,000.
    /// When exceeded after time-based pruning, the oldest entries are removed first.
    /// </summary>
    public int MaxEntryCount { get; set; } = 50_000;

    /// <summary>
    /// Capacity of the in-memory channel buffer between the request path and the background flusher.
    /// When the buffer is full the oldest entry is dropped. Default: 10,000.
    /// </summary>
    public int ChannelCapacity { get; set; } = 10_000;

    /// <summary>
    /// Reserved — wired in M2.6.
    /// Callback invoked on the request path to apply custom tags to each entry before enqueue.
    /// </summary>
    public Action<LookoutEntry, IDictionary<string, string>>? Tag { get; set; }

    /// <summary>
    /// Reserved — wired in M2.6.
    /// Per-entry filter: return <c>false</c> to drop an entry before it is enqueued.
    /// Runs synchronously on the request path; keep it fast.
    /// </summary>
    public Func<LookoutEntry, bool>? Filter { get; set; }

    /// <summary>
    /// Reserved — wired in M2.6.
    /// Batch-level filter applied in the flusher before writing to storage.
    /// May be slower than <see cref="Filter"/>; runs off the request path.
    /// </summary>
    public Func<IReadOnlyList<LookoutEntry>, IReadOnlyList<LookoutEntry>>? FilterBatch { get; set; }

    /// <summary>
    /// Reserved — wired in M2.6.
    /// Redaction configuration for headers, query parameters, form fields, and custom callbacks.
    /// </summary>
    public RedactionOptions Redaction { get; set; } = new();
}
