using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Lookout.Core.Diagnostics;

/// <summary>
/// Per-request N+1 detection scope, scoped via <see cref="AsyncLocal{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// The middleware calls <see cref="Begin"/> before invoking <c>_next</c>. The EF Core
/// interceptor and ADO.NET subscriber call <see cref="Track"/> (when
/// <see cref="Current"/> is non-null) instead of recording directly to the recorder.
/// The middleware then calls <see cref="Complete"/> in its <c>finally</c> block to flush
/// buffered entries — with N+1 tags applied — and dispose the scope.
/// </para>
/// <para>
/// Out-of-request DB calls (no active scope) fall back to recording directly via the
/// recorder as before. N+1 detection is skipped for those calls.
/// </para>
/// <para>
/// Not thread-safe — designed for the single async flow of an ASP.NET Core request.
/// </para>
/// </remarks>
public sealed class N1RequestScope : IDisposable
{
    private static readonly AsyncLocal<N1RequestScope?> _current = new();

    /// <summary>The active scope for the current async execution context, or <c>null</c> if outside a request.</summary>
    public static N1RequestScope? Current => _current.Value;

    private readonly N1Detector _detector;
    private readonly List<(LookoutEntry Entry, string ShapeKey)> _pending = new();

    /// <summary>Total DB entries buffered in this scope.</summary>
    public int DbCount => _pending.Count;

    private N1RequestScope(EfOptions options)
    {
        _detector = new N1Detector(options.N1DetectionMinOccurrences, options.N1IgnorePatterns);
    }

    /// <summary>Creates and installs a new scope for the current async flow.</summary>
    public static N1RequestScope Begin(EfOptions options)
    {
        var scope = new N1RequestScope(options);
        _current.Value = scope;
        return scope;
    }

    /// <summary>
    /// Buffers a DB entry for N+1 tracking. Called by the EF interceptor and ADO.NET subscriber
    /// instead of recording directly to the recorder when a scope is active.
    /// </summary>
    public void Track(LookoutEntry entry, string commandText)
    {
        var shapeKey = SqlNormaliser.Normalise(commandText);
        _pending.Add((entry, shapeKey));
        // Use 1-based list index as the stable entry ID within this scope.
        _detector.Track(shapeKey, _pending.Count);
    }

    /// <summary>
    /// Runs N+1 detection, applies <c>n1.group</c> and <c>n1.count</c> tags to entries
    /// in detected groups, and flushes all buffered entries to <paramref name="recorder"/>.
    /// </summary>
    /// <returns>Detected N+1 groups (empty when none meet the threshold).</returns>
    public IReadOnlyList<N1Group> Complete(ILookoutRecorder recorder)
    {
        var groups = _detector.Detect();

        var groupByShape = new Dictionary<string, N1Group>(StringComparer.Ordinal);
        foreach (var g in groups)
            groupByShape[g.ShapeKey] = g;

        foreach (var (entry, shapeKey) in _pending)
        {
            LookoutEntry final;
            if (groupByShape.TryGetValue(shapeKey, out var group))
            {
                var tags = entry.Tags.ToDictionary(
                        kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);
                tags["n1.group"] = ComputeShapeHash(shapeKey);
                tags["n1.count"] = group.Count.ToString(CultureInfo.InvariantCulture);
                final = entry with { Tags = tags };
            }
            else
            {
                final = entry;
            }

            recorder.Record(final);
        }

        return groups;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _current.Value = null;
    }

    /// <summary>
    /// Returns a stable 8-character lowercase hex hash of <paramref name="shapeKey"/>,
    /// used as the <c>n1.group</c> tag value to identify entries belonging to the same group.
    /// </summary>
    internal static string ComputeShapeHash(string shapeKey)
    {
        var bytes = Encoding.UTF8.GetBytes(shapeKey);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(bytes);
        // 4 bytes → 8 hex chars; stable and short enough for a UI grouping key.
        return BitConverter.ToString(hash, 0, 4).Replace("-", string.Empty)
                           .ToLowerInvariant();
    }
}
