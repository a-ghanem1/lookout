using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Lookout.Core.Schemas;

namespace Lookout.Core.Diagnostics;

/// <summary>
/// Captures filtered user-code stack frames, excluding known framework assemblies.
/// Shared across capture types (EF, outbound HTTP, exceptions).
/// </summary>
public static class StackTraceCapture
{
    // Cache resolved method names; file+line come from the live frame.
    private static readonly ConcurrentDictionary<MethodBase, string> _nameCache = new();

    // Prefix-matched: catches all sub-assemblies (e.g. Microsoft.EntityFrameworkCore.Sqlite).
    private static readonly string[] _noisePrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "System",
        "Microsoft.AspNetCore",
    ];

    // Exact-matched: product assemblies only — prevents test / consumer assemblies whose names
    // begin with "Lookout." from being incorrectly treated as noise.
    private static readonly HashSet<string> _noiseExact = new(StringComparer.OrdinalIgnoreCase)
    {
        "Lookout.Core",
        "Lookout.EntityFrameworkCore",
        "Lookout.AspNetCore",
        "Lookout.Storage.Sqlite",
        "Lookout.Dashboard",
        "Lookout.Benchmarks",
    };

    /// <summary>
    /// Returns up to <paramref name="maxFrames"/> user-code frames, skipping
    /// the first <paramref name="skipFrames"/> frames from the top of the stack.
    /// </summary>
    public static IReadOnlyList<EfStackFrame> Capture(int skipFrames, int maxFrames)
    {
        if (maxFrames <= 0) return [];

        var trace = new StackTrace(skipFrames + 1, fNeedFileInfo: true);
        var frames = trace.GetFrames();
        if (frames == null || frames.Length == 0) return [];

        var result = new List<EfStackFrame>(Math.Min(maxFrames, frames.Length));
        foreach (var frame in frames)
        {
            if (result.Count >= maxFrames) break;

            var method = frame.GetMethod();
            if (method == null) continue;
            if (IsNoiseFrame(method)) continue;

            var name = _nameCache.GetOrAdd(method, ResolveMethodName);
            var file = frame.GetFileName();
            var line = frame.GetFileLineNumber();

            result.Add(new EfStackFrame(
                name,
                string.IsNullOrEmpty(file) ? null : file,
                line == 0 ? null : (int?)line));
        }

        return result;
    }

    /// <summary>Returns <c>true</c> if the assembly name belongs to a known noise set.</summary>
    internal static bool IsNoiseAssembly(string assemblyName)
    {
        if (_noiseExact.Contains(assemblyName)) return true;
        foreach (var prefix in _noisePrefixes)
        {
            if (assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsNoiseFrame(MethodBase method)
    {
        var name = method.DeclaringType?.Assembly.GetName().Name ?? string.Empty;
        return IsNoiseAssembly(name);
    }

    private static string ResolveMethodName(MethodBase method)
    {
        var typeName = method.DeclaringType?.FullName;
        return typeName != null ? $"{typeName}.{method.Name}" : method.Name;
    }
}
