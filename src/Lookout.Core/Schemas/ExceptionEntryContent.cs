namespace Lookout.Core.Schemas;

/// <summary>
/// JSON shape stored in <see cref="LookoutEntry.Content"/> for entries of type <c>exception</c>.
/// </summary>
/// <param name="ExceptionType">Full CLR type name (e.g. <c>System.InvalidOperationException</c>).</param>
/// <param name="Message">Exception message.</param>
/// <param name="Stack">Filtered user-code stack frames — framework and Lookout frames stripped.</param>
/// <param name="InnerExceptions">First-level inner exceptions only (no recursion).</param>
/// <param name="Source">Assembly that threw the exception, or <c>null</c> when unavailable.</param>
/// <param name="HResult">Win32 error code associated with this exception.</param>
/// <param name="Handled">
/// <c>true</c> when captured via <c>IExceptionHandler</c> (exception was handled by the host);
/// <c>false</c> when captured via the diagnostic listener (exception propagated unhandled).
/// </param>
public sealed record ExceptionEntryContent(
    string ExceptionType,
    string Message,
    IReadOnlyList<EfStackFrame> Stack,
    IReadOnlyList<InnerExceptionSummary> InnerExceptions,
    string? Source,
    int HResult,
    bool Handled);

/// <summary>Short summary of a first-level inner exception.</summary>
/// <param name="Type">Full CLR type name of the inner exception.</param>
/// <param name="Message">Inner exception message.</param>
public sealed record InnerExceptionSummary(string Type, string Message);
