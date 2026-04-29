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
    /// Callback invoked on the request path to apply custom tags to each entry before enqueue.
    /// The second argument is a mutable copy of the entry's tag dictionary; mutate it freely.
    /// </summary>
    public Action<LookoutEntry, IDictionary<string, string>>? Tag { get; set; }

    /// <summary>
    /// Per-entry filter: return <c>false</c> to drop an entry before it is enqueued.
    /// Runs synchronously on the request path; keep it fast.
    /// </summary>
    public Func<LookoutEntry, bool>? Filter { get; set; }

    /// <summary>
    /// Batch-level filter applied in the flusher before writing to storage.
    /// May be slower than <see cref="Filter"/>; runs off the request path.
    /// </summary>
    public Func<IReadOnlyList<LookoutEntry>, IReadOnlyList<LookoutEntry>>? FilterBatch { get; set; }

    /// <summary>Redaction configuration for headers, query parameters, and form fields.</summary>
    public RedactionOptions Redaction { get; set; } = new();

    /// <summary>EF Core capture configuration.</summary>
    public EfOptions Ef { get; set; } = new();

    /// <summary>Outbound HttpClient capture configuration.</summary>
    public HttpOptions Http { get; set; } = new();

    /// <summary>Cache capture configuration.</summary>
    public CacheOptions Cache { get; set; } = new();

    /// <summary>Exception capture configuration.</summary>
    public ExceptionOptions Exceptions { get; set; } = new();

    /// <summary>Log capture configuration.</summary>
    public LoggingOptions Logging { get; set; } = new();

    /// <summary>
    /// Custom redaction callback applied in the flusher after the default redactors.
    /// Return a (possibly modified) entry; return the input unchanged to pass through.
    /// </summary>
    public Func<LookoutEntry, LookoutEntry>? Redact { get; set; }

    /// <summary>How often the retention background service runs. Default: 5 minutes.</summary>
    /// <remarks>Exposed primarily for testing; production callers should not need to change this.</remarks>
    internal TimeSpan RetentionInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// When true, HTTP request bodies are buffered and captured (content-type gated,
    /// size-capped by <see cref="MaxBodyBytes"/>). Default: <c>false</c>.
    /// </summary>
    public bool CaptureRequestBody { get; set; }

    /// <summary>
    /// When true, HTTP response bodies are captured (content-type gated,
    /// size-capped by <see cref="MaxBodyBytes"/>). Default: <c>false</c>.
    /// </summary>
    public bool CaptureResponseBody { get; set; }

    /// <summary>
    /// Maximum number of bytes captured per request or response body.
    /// Bodies larger than this are truncated with a marker. Default: 65,536 (64 KiB).
    /// </summary>
    public int MaxBodyBytes { get; set; } = 65_536;

    /// <summary>
    /// Request paths that are skipped entirely by HTTP capture. Comparison is case-insensitive.
    /// The <c>/lookout</c> mount prefix is always skipped regardless of this set.
    /// </summary>
    public HashSet<string> SkipPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "/healthz", "/health", "/ready", "/favicon.ico",
    };

    /// <summary>
    /// Content types eligible for body capture. Matches exact strings and a single
    /// <c>text/*</c> wildcard. Comparison is case-insensitive.
    /// </summary>
    public HashSet<string> CapturedContentTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/json", "application/x-www-form-urlencoded", "text/*",
    };

    /// <summary>
    /// Claim type used to resolve the authenticated user name for HTTP capture.
    /// When <c>null</c> (default), <c>HttpContext.User.Identity?.Name</c> is used.
    /// </summary>
    public string? UserClaimType { get; set; }
}
