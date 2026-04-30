using System.Diagnostics;
using System.Text.Json;
using Lookout.Core;
using Lookout.Core.Diagnostics;
using Lookout.Core.Schemas;

namespace Lookout.AspNetCore.Capture.Exceptions;

/// <summary>Shared entry-building logic used by both exception capture paths.</summary>
internal static class ExceptionCapture
{
    internal static LookoutEntry BuildEntry(Exception exception, bool handled, int maxStackFrames)
    {
        var exType = exception.GetType().FullName ?? exception.GetType().Name;

        InnerExceptionSummary[] innerExceptions = exception.InnerException is not null
            ? [new InnerExceptionSummary(
                exception.InnerException.GetType().FullName ?? exception.InnerException.GetType().Name,
                exception.InnerException.Message)]
            : [];

        var content = new ExceptionEntryContent(
            ExceptionType: exType,
            Message: exception.Message,
            Stack: StackTraceCapture.CaptureFromException(exception, maxStackFrames),
            InnerExceptions: innerExceptions,
            Source: exception.Source,
            HResult: exception.HResult,
            Handled: handled);

        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["exception.type"] = exType,
            ["exception.handled"] = handled ? "true" : "false",
        };
        if (!string.IsNullOrEmpty(exception.Source))
            tags["exception.source"] = exception.Source!;

        return new LookoutEntry(
            Id: Guid.NewGuid(),
            Type: "exception",
            Timestamp: DateTimeOffset.UtcNow,
            RequestId: Activity.Current?.RootId,
            DurationMs: 0,
            Tags: tags,
            Content: JsonSerializer.Serialize(content, LookoutJson.Options));
    }

    internal static bool IsIgnored(Exception exception, IList<string> ignoreTypes)
    {
        var exType = exception.GetType();
        foreach (var ignored in ignoreTypes)
        {
            var current = exType;
            while (current is not null)
            {
                if (string.Equals(current.FullName, ignored, StringComparison.Ordinal))
                    return true;
                current = current.BaseType;
            }
        }
        return false;
    }
}
