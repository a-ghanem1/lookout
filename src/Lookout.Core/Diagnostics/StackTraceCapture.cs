using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
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

    // Matches standard async/iterator state machine types: <MethodName>d__N
    // Inner name must not contain angle brackets — lambda state machines (which contain <> in
    // their compiler-generated names) don't match and are rendered as "<lambda>" instead.
    private static readonly Regex _stateMachineRx =
        new(@"^<([^<>]+)>d__\d+$", RegexOptions.Compiled);

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
    /// Returns up to <paramref name="maxFrames"/> filtered user-code frames extracted from
    /// <paramref name="exception"/>'s stack trace. Framework and Lookout frames are stripped.
    /// </summary>
    public static IReadOnlyList<EfStackFrame> CaptureFromException(Exception exception, int maxFrames)
    {
        if (maxFrames <= 0) return [];

        var trace = new StackTrace(exception, fNeedFileInfo: true);
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
            var ef = new EfStackFrame(
                name,
                string.IsNullOrEmpty(file) ? null : file,
                line == 0 ? null : (int?)line);

            if (!IsDuplicateFrame(result, ef)) result.Add(ef);
        }

        return result;
    }

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
            var ef = new EfStackFrame(
                name,
                string.IsNullOrEmpty(file) ? null : file,
                line == 0 ? null : (int?)line);

            if (!IsDuplicateFrame(result, ef)) result.Add(ef);
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

    // Suppress a frame that duplicates the previous entry's method name but adds no new
    // file/line info. This collapses the two frames that the C# compiler emits for a lambda
    // (one with source coordinates, one without) into a single readable entry.
    private static bool IsDuplicateFrame(List<EfStackFrame> result, EfStackFrame candidate)
    {
        if (result.Count == 0) return false;
        var prev = result[^1];
        return prev.Method == candidate.Method && candidate.File is null;
    }

    private static bool IsNoiseFrame(MethodBase method)
    {
        // DynamicMethod (Reflection.Emit / expression-tree delegates) has no DeclaringType —
        // these are runtime dispatch machinery, not user code.
        if (method.DeclaringType is null) return true;
        var name = method.DeclaringType.Assembly.GetName().Name ?? string.Empty;
        return IsNoiseAssembly(name);
    }

    private static string ResolveMethodName(MethodBase method)
    {
        // Async/iterator state machines: MoveNext → unwrap to real method name.
        if (method.Name == "MoveNext" && method.DeclaringType is { } dt)
        {
            var clean = TryUnwrapStateMachine(dt);
            if (clean is not null) return clean;
        }

        // Compiler-generated lambda/anonymous methods (b__N) have names starting with '<'.
        // Render as OwnerType.<lambda> — the file+line on the frame is the useful signal.
        if (method.Name.StartsWith("<", StringComparison.Ordinal) && method.DeclaringType is { } lt)
        {
            // <>c / <>c__DisplayClass are closure-capture types; walk up to the real owner.
            var owner = lt.Name.StartsWith("<>", StringComparison.Ordinal)
                ? lt.DeclaringType ?? lt
                : lt;
            return $"{(owner.FullName ?? owner.Name).Replace('+', '.')}.<lambda>";
        }

        var typeName = method.DeclaringType?.FullName;
        return typeName != null ? $"{typeName}.{method.Name}" : method.Name;
    }

    /// <summary>
    /// Attempts to resolve the user-visible method name from a compiler-generated async or
    /// iterator state machine type. Returns <c>null</c> if the type doesn't look like a
    /// state machine (lets the caller fall back to the raw type+method string).
    /// </summary>
    private static string? TryUnwrapStateMachine(Type smType)
    {
        if (!smType.Name.StartsWith("<", StringComparison.Ordinal)) return null;

        // Walk up through compiler-generated wrapper types such as <>c / <>c__DisplayClass.
        var owner = smType.DeclaringType;
        while (owner is not null && owner.Name.StartsWith("<>", StringComparison.Ordinal))
            owner = owner.DeclaringType;

        if (owner is null) return null;

        // For standard named async methods the state machine type is <MethodName>d__N.
        // Lambda state machines embed angle brackets in the inner name and don't match —
        // they fall back to the generic "<lambda>" label.
        var m = _stateMachineRx.Match(smType.Name);
        var methodPart = m.Success ? m.Groups[1].Value : "<lambda>";

        // Replace the + nested-type separator with . for a Java-style qualified name.
        var ownerName = (owner.FullName ?? owner.Name).Replace('+', '.');
        return $"{ownerName}.{methodPart}";
    }
}
