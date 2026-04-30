namespace Lookout.Core;

/// <summary>Dump capture configuration.</summary>
public sealed class DumpOptions
{
    /// <summary>
    /// When <c>true</c>, <see cref="Lookout.Dump"/> entries are captured. Default: <c>true</c>.
    /// </summary>
    public bool Capture { get; set; } = true;

    /// <summary>
    /// Maximum number of characters serialised per dump entry before truncation.
    /// Default: 65,536 (64 KiB equivalent in ASCII JSON).
    /// </summary>
    public int MaxBytes { get; set; } = 65_536;
}
