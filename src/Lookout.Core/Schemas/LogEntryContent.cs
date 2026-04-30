namespace Lookout.Core.Schemas;

/// <summary>
/// JSON shape stored in <see cref="LookoutEntry.Content"/> for entries of type <c>log</c>.
/// </summary>
/// <param name="Level">Log level as a string (e.g. <c>"Information"</c>, <c>"Warning"</c>).</param>
/// <param name="Category">Logger category name (typically the fully-qualified type name).</param>
/// <param name="Message">Fully rendered log message.</param>
/// <param name="EventId">Structured event id, when present.</param>
/// <param name="Scopes">Active scope frames at the time of the log call, capped at <c>MaxScopeFrames</c>.</param>
/// <param name="ExceptionType">
/// Full CLR type name of the exception passed to the log call, or <c>null</c>.
/// Short-form only — full stack details live in the companion <c>exception</c> entry.
/// </param>
/// <param name="ExceptionMessage">Exception message, or <c>null</c> when no exception was passed.</param>
public sealed record LogEntryContent(
    string Level,
    string Category,
    string Message,
    LogEventId EventId,
    IReadOnlyList<string> Scopes,
    string? ExceptionType,
    string? ExceptionMessage);

/// <summary>Structured event id captured from a log call.</summary>
/// <param name="Id">Numeric event id. Zero when not explicitly set.</param>
/// <param name="Name">Optional symbolic event name.</param>
public sealed record LogEventId(int Id, string? Name);
