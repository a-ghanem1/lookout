using Microsoft.Extensions.Logging;

namespace Lookout.Core;

/// <summary>Log capture configuration.</summary>
public sealed class LoggingOptions
{
    /// <summary>
    /// When <c>true</c>, log entries are captured. Default: <c>true</c>.
    /// </summary>
    public bool Capture { get; set; } = true;

    /// <summary>
    /// Minimum log level captured. Default: <see cref="LogLevel.Information"/>.
    /// Raise to <see cref="LogLevel.Warning"/> to suppress high-volume informational chatter.
    /// </summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    /// <summary>
    /// Logger category name patterns whose entries are dropped before recording.
    /// A trailing <c>*</c> is a prefix wildcard. Comparison is case-insensitive.
    /// Default: <c>Microsoft.*</c> and <c>System.*</c> — framework chatter is either
    /// duplicated by dedicated Lookout capture points (EF, HTTP, cache) or is infrastructure
    /// noise irrelevant to the developer's own application code.
    /// </summary>
    public IList<string> IgnoreCategories { get; set; } =
    [
        "Microsoft.*",
        "System.*",
    ];

    /// <summary>
    /// Maximum number of active scope frames captured per log entry. Default: <c>5</c>.
    /// </summary>
    public int MaxScopeFrames { get; set; } = 5;
}
