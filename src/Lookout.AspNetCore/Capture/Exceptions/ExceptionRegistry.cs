using System.Runtime.CompilerServices;

namespace Lookout.AspNetCore.Capture.Exceptions;

/// <summary>
/// Tracks exceptions that have already been captured so both capture paths can deduplicate.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="LookoutExceptionHandler"/> stamps an exception when it records it.
/// <see cref="UnhandledExceptionDiagnosticSubscriber"/> checks <see cref="IsStamped"/> and
/// skips the exception if already recorded. This guarantees at most one entry per throw.
/// </para>
/// <para>
/// Uses a <see cref="ConditionalWeakTable{TKey,TValue}"/> so entries are collected with
/// the exception object — no memory leak risk.
/// </para>
/// </remarks>
internal static class ExceptionRegistry
{
    private static readonly ConditionalWeakTable<Exception, object> _captured = new();
    private static readonly object _sentinel = new();

    /// <summary>
    /// Stamps <paramref name="exception"/> as captured. Returns <c>true</c> if this is the first
    /// stamp (new exception); <c>false</c> if already stamped.
    /// </summary>
    internal static bool TryStamp(Exception exception) =>
        _captured.TryAdd(exception, _sentinel);

    /// <summary>Returns <c>true</c> when the exception has already been stamped.</summary>
    internal static bool IsStamped(Exception exception) =>
        _captured.TryGetValue(exception, out _);
}
