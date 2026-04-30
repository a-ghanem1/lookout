using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Lookout.Core.Diagnostics;
using Lookout.Core.Schemas;

namespace Lookout.Core;

/// <summary>
/// Developer-facing static helper. Call <see cref="Dump"/> anywhere in domain code
/// to ship an object snapshot to the Lookout dashboard, correlated to the current request.
/// Outside of a request context the call is a silent no-op — safe to leave in prod-facing code
/// behind a feature flag or environment guard.
/// </summary>
public static class Lookout
{
    /// <summary>
    /// Captures <paramref name="value"/> as a JSON snapshot in the current request's Lookout entry.
    /// Preserves the compile-time type <typeparamref name="T"/> — useful when <paramref name="value"/>
    /// is typed as an interface or base class.
    /// </summary>
    public static void Dump<T>(
        T value,
        string? label = null,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0,
        [CallerMemberName] string callerMember = "")
        => DumpCore((object?)value, typeof(T), label, callerFile, callerLine, callerMember);

    /// <summary>
    /// Captures <paramref name="value"/> as a JSON snapshot in the current request's Lookout entry.
    /// </summary>
    public static void Dump(
        object? value,
        string? label = null,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0,
        [CallerMemberName] string callerMember = "")
        => DumpCore(value, value?.GetType(), label, callerFile, callerLine, callerMember);

    private static void DumpCore(
        object? value,
        Type? runtimeType,
        string? label,
        string callerFile,
        int callerLine,
        string callerMember)
    {
        var recorder = DumpRecorder.Recorder;
        if (recorder is null) return;

        try
        {
            var maxBytes = DumpRecorder.MaxBytes;
            var valueType = FormatValueTypeName(runtimeType);

            string json;
            var truncated = false;
            try
            {
                var raw = JsonSerializer.Serialize(value, LookoutJson.Options);
                if (raw.Length > maxBytes)
                {
                    json = raw.Substring(0, maxBytes);
                    truncated = true;
                }
                else
                {
                    json = raw;
                }
            }
            catch (Exception ex)
            {
                json = $"[lookout: serialization failed: {ex.GetType().Name}]";
            }

            var content = new DumpEntryContent(
                Label: label,
                Json: json,
                JsonTruncated: truncated,
                ValueType: valueType,
                CallerFile: callerFile,
                CallerLine: callerLine,
                CallerMember: callerMember);

            var tags = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dump"] = "true",
                ["dump.type"] = valueType,
            };
            if (!string.IsNullOrEmpty(label))
                tags["dump.label"] = label!;

            var entry = new LookoutEntry(
                Id: Guid.NewGuid(),
                Type: "dump",
                Timestamp: DateTimeOffset.UtcNow,
                RequestId: Activity.Current?.RootId,
                DurationMs: 0,
                Tags: tags,
                Content: JsonSerializer.Serialize(content, LookoutJson.Options));

            N1RequestScope.Current?.TrackDump();
            recorder.Record(entry);
        }
        catch
        {
            // Never propagate from Dump
        }
    }

    private static string FormatValueTypeName(Type? type)
    {
        if (type is null) return "<null>";
        // Compiler-generated anonymous types: <>f__AnonymousType0`N
        if (type.Name.StartsWith("<>f__AnonymousType", StringComparison.Ordinal))
            return "{ anonymous }";
        return type.FullName ?? type.Name;
    }
}
