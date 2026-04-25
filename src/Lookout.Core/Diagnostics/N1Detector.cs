using System.Text.RegularExpressions;

namespace Lookout.Core.Diagnostics;

/// <summary>
/// Represents a group of structurally identical queries detected as a potential N+1.
/// </summary>
/// <param name="ShapeKey">The normalised SQL shape that was repeated.</param>
/// <param name="Count">Number of times the shape was executed.</param>
/// <param name="EntryIds">Storage IDs of the individual DB entries in this group.</param>
public sealed record N1Group(string ShapeKey, int Count, IReadOnlyList<long> EntryIds);

/// <summary>
/// Stateful per-request N+1 query detector.
/// </summary>
/// <remarks>
/// Not thread-safe — designed for single-request scope where all DB events arrive
/// serially (e.g. via middleware or an interceptor running on the request thread).
/// Create one instance per request; discard after the request completes.
/// </remarks>
public sealed class N1Detector
{
    private readonly int _minOccurrences;
    private readonly IList<Regex> _ignorePatterns;

    // shape key → ordered list of entry IDs
    private readonly Dictionary<string, List<long>> _groups =
        new(StringComparer.Ordinal);

    /// <param name="minOccurrences">
    /// Minimum number of executions of the same SQL shape before a group is reported.
    /// </param>
    /// <param name="ignorePatterns">
    /// Shape keys matching any of these patterns are excluded from detection.
    /// </param>
    public N1Detector(int minOccurrences = 3, IList<Regex>? ignorePatterns = null)
    {
        _minOccurrences = minOccurrences;
        _ignorePatterns = ignorePatterns ?? [];
    }

    /// <summary>
    /// Records a DB entry for the given normalised shape key.
    /// Call this after each DB entry is persisted so the entry ID is known.
    /// </summary>
    public void Track(string shapeKey, long entryId)
    {
        if (!_groups.TryGetValue(shapeKey, out var ids))
        {
            ids = [];
            _groups[shapeKey] = ids;
        }

        ids.Add(entryId);
    }

    /// <summary>
    /// Returns all shape groups whose execution count meets or exceeds
    /// <c>MinOccurrences</c> and are not suppressed by an ignore pattern.
    /// </summary>
    public IReadOnlyList<N1Group> Detect()
    {
        var result = new List<N1Group>();

        foreach (var kvp in _groups)
        {
            if (kvp.Value.Count < _minOccurrences) continue;
            if (IsIgnored(kvp.Key)) continue;

            result.Add(new N1Group(kvp.Key, kvp.Value.Count, kvp.Value.AsReadOnly()));
        }

        return result;
    }

    private bool IsIgnored(string shapeKey)
    {
        foreach (var pattern in _ignorePatterns)
        {
            if (pattern.IsMatch(shapeKey))
                return true;
        }

        return false;
    }
}
