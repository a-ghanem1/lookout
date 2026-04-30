namespace Lookout.Core.Schemas;

/// <summary>A single argument passed to a Hangfire job at enqueue time.</summary>
/// <param name="Name">Parameter name from the method signature.</param>
/// <param name="Type">Full CLR type name of the parameter.</param>
/// <param name="Value">
/// JSON-rendered value, redacted per <see cref="RedactionOptions"/>, truncated at
/// <see cref="HangfireOptions.ArgumentMaxBytes"/> with a truncation marker.
/// </param>
public sealed record JobArgument(string Name, string Type, string Value);

/// <summary>
/// JSON shape stored in <see cref="LookoutEntry.Content"/> for entries of type <c>job-enqueue</c>.
/// </summary>
/// <param name="JobId">Hangfire job id assigned at enqueue time.</param>
/// <param name="JobType">Full CLR type name of the job class, or <c>null</c> for lambda jobs.</param>
/// <param name="MethodName">Name of the method that will be invoked by the worker.</param>
/// <param name="Arguments">Captured argument values (redacted + truncated).</param>
/// <param name="Queue">Target queue, e.g. <c>default</c> or <c>critical</c>.</param>
/// <param name="State">Initial job state, e.g. <c>Enqueued</c>, <c>Scheduled</c>, <c>Awaiting</c>.</param>
/// <param name="ErrorType">Full CLR exception type name when job creation failed; otherwise <c>null</c>.</param>
/// <param name="ErrorMessage">Exception message when job creation failed; otherwise <c>null</c>.</param>
public sealed record JobEnqueueEntryContent(
    string JobId,
    string? JobType,
    string MethodName,
    IReadOnlyList<JobArgument> Arguments,
    string Queue,
    string State,
    string? ErrorType,
    string? ErrorMessage);
