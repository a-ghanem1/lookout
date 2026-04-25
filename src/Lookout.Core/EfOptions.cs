using System.Text.RegularExpressions;

namespace Lookout.Core;

/// <summary>EF Core capture configuration.</summary>
public sealed class EfOptions
{
    /// <summary>
    /// When <c>true</c>, actual SQL parameter values are captured (redaction applied).
    /// Default: <c>true</c>.
    /// </summary>
    public bool CaptureParameterValues { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, only parameter type names are captured; values are never stored.
    /// Takes precedence over <see cref="CaptureParameterValues"/>. Default: <c>false</c>.
    /// </summary>
    public bool CaptureParameterTypesOnly { get; set; }

    /// <summary>
    /// Maximum number of user-code stack frames to capture per query. Default: <c>20</c>.
    /// </summary>
    public int MaxStackFrames { get; set; } = 20;

    /// <summary>
    /// Minimum number of executions of the same SQL shape (within a single request)
    /// before the group is flagged as an N+1. Default: <c>3</c>.
    /// </summary>
    public int N1DetectionMinOccurrences { get; set; } = 3;

    /// <summary>
    /// SQL shape keys matching any of these patterns are excluded from N+1 detection.
    /// Default: empty (no exclusions).
    /// </summary>
    public IList<Regex> N1IgnorePatterns { get; set; } = [];
}
