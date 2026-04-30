namespace Lookout.Core;

/// <summary>Hangfire background-job capture configuration.</summary>
public sealed class HangfireOptions
{
    /// <summary>Master switch for Hangfire capture. Default: <c>true</c>.</summary>
    public bool Capture { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, job argument values are serialised and stored.
    /// Default: <c>true</c>.
    /// </summary>
    public bool CaptureArguments { get; set; } = true;

    /// <summary>
    /// Maximum number of UTF-8 bytes captured per job argument value before truncation.
    /// Smaller than the 64 KiB body cap because a job may have many arguments.
    /// Default: 8,192.
    /// </summary>
    public int ArgumentMaxBytes { get; set; } = 8_192;

    /// <summary>
    /// Full CLR type names of job classes to skip. Matching jobs (and any subclasses)
    /// are silently dropped before recording. Useful for silencing chatty heartbeat jobs.
    /// Default: empty (no exclusions).
    /// </summary>
    public IList<string> IgnoreJobTypes { get; set; } = [];
}
