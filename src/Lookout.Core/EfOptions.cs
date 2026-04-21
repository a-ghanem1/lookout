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
}
