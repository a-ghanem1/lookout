using System.Reflection;

namespace Lookout.Dashboard;

/// <summary>
/// Marker and accessor for the embedded Vite bundle. The dashboard project embeds
/// <c>wwwroot/**</c> with resource names prefixed <c>LookoutUi/</c>; callers look up
/// files by their relative path (e.g. <c>index.html</c>, <c>assets/index-abc.js</c>).
/// </summary>
public static class DashboardAssets
{
    private const string ResourcePrefix = "LookoutUi/";

    /// <summary>The assembly whose manifest carries the dashboard bundle.</summary>
    public static Assembly Assembly => typeof(DashboardAssets).Assembly;

    // On Windows, MSBuild's %(RecursiveDir) uses backslashes in LogicalName, so embedded
    // resource names may be "LookoutUi/assets\foo.js" while callers always use '/'.
    // Map forward-slash-normalized keys → original names for GetManifestResourceStream.
    private static readonly Dictionary<string, string> _normalizedNames = BuildNormalizedNames();

    private static Dictionary<string, string> BuildNormalizedNames()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in Assembly.GetManifestResourceNames())
        {
            if (name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                map[name.Replace('\\', '/')] = name;
        }
        return map;
    }

    /// <summary>
    /// Returns a stream for the embedded file at <paramref name="relativePath"/>, or
    /// <c>null</c> when no asset with that name is embedded.
    /// </summary>
    public static Stream? Open(string relativePath)
    {
        var normalized = BuildResourceName(relativePath);
        return _normalizedNames.TryGetValue(normalized, out var actual)
            ? Assembly.GetManifestResourceStream(actual)
            : null;
    }

    /// <summary>Enumerates every embedded dashboard asset, path-only (always forward-slash separated).</summary>
    public static IEnumerable<string> EnumerateAssetPaths()
    {
        foreach (var name in Assembly.GetManifestResourceNames())
        {
            if (name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                yield return name.Substring(ResourcePrefix.Length).Replace('\\', '/');
        }
    }

    internal static string BuildResourceName(string relativePath)
    {
        // Normalize: strip leading '/', fold '\\' → '/'.
        var cleaned = relativePath.Replace('\\', '/').TrimStart('/');
        return ResourcePrefix + cleaned;
    }
}
