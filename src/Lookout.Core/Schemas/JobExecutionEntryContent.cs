namespace Lookout.Core.Schemas;

/// <summary>
/// JSON shape stored in <see cref="LookoutEntry.Content"/> for entries of type <c>job-execution</c>.
/// </summary>
/// <param name="JobId">Hangfire job id.</param>
/// <param name="JobType">Full CLR type name of the job class, or <c>null</c> for lambda jobs.</param>
/// <param name="MethodName">Name of the executed method.</param>
/// <param name="EnqueueRequestId">
/// Root activity id of the HTTP request that enqueued this job, or <c>null</c> when the job
/// was enqueued outside a tracked request (e.g. scheduled, re-queued by Hangfire retry).
/// </param>
/// <param name="State">Terminal job state: <c>Succeeded</c> or <c>Failed</c>.</param>
/// <param name="ErrorType">Full CLR exception type name on failure; otherwise <c>null</c>.</param>
/// <param name="ErrorMessage">Exception message on failure; otherwise <c>null</c>.</param>
public sealed record JobExecutionEntryContent(
    string JobId,
    string? JobType,
    string MethodName,
    string? EnqueueRequestId,
    string State,
    string? ErrorType,
    string? ErrorMessage);
