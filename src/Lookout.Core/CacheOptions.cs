using System.Text.RegularExpressions;

namespace Lookout.Core;

/// <summary>Cache capture configuration.</summary>
public sealed class CacheOptions
{
    /// <summary>
    /// When <c>true</c>, <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/> operations
    /// are captured. Default: <c>true</c>.
    /// </summary>
    public bool CaptureMemoryCache { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>
    /// operations are captured. Default: <c>true</c>.
    /// </summary>
    public bool CaptureDistributedCache { get; set; } = true;

    /// <summary>
    /// Cache keys matching any pattern in this list are replaced with <c>***</c> before storage.
    /// Default: empty (no redaction).
    /// </summary>
    public IList<Regex> SensitiveKeyPatterns { get; set; } = [];
}
