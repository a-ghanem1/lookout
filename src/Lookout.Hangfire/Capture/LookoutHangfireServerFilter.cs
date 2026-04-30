using System.Diagnostics;
using System.Text.Json;
using Hangfire.Server;
using Lookout.Core;
using Lookout.Core.Diagnostics;
using Lookout.Core.Schemas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lookout.Hangfire;

/// <summary>
/// Server-side Hangfire filter. Opens an <see cref="N1RequestScope"/> for each job execution
/// so that EF/ADO.NET queries issued by the job get N+1 detection and are flushed when the
/// job completes. Records a <c>job-execution</c> entry correlated back to the originating
/// HTTP request via the <c>lookout.enqueue.request-id</c> job parameter.
/// </summary>
internal sealed class LookoutHangfireServerFilter : IServerFilter
{
    private const string StartKey = "lookout.server.start";
    private const string ScopeKey = "lookout.server.scope";
    private const string EnqueueRequestIdKey = "lookout.server.enqueue-rid";

    private readonly ILookoutRecorder _recorder;
    private readonly HangfireOptions _hangfireOptions;
    private readonly EfOptions _efOptions;
    private readonly ILogger<LookoutHangfireServerFilter> _logger;

    public LookoutHangfireServerFilter(
        ILookoutRecorder recorder,
        IOptions<LookoutOptions> options,
        ILogger<LookoutHangfireServerFilter> logger)
    {
        _recorder = recorder;
        _hangfireOptions = options.Value.Hangfire;
        _efOptions = options.Value.Ef;
        _logger = logger;
    }

    public void OnPerforming(PerformingContext filterContext)
    {
        if (!_hangfireOptions.Capture) return;

        filterContext.Items[StartKey] = Stopwatch.GetTimestamp();

        var enqueueRequestId = filterContext.GetJobParameter<string>(
            LookoutHangfireClientFilter.EnqueueRequestIdParam);
        filterContext.Items[EnqueueRequestIdKey] = enqueueRequestId ?? string.Empty;

        filterContext.Items[ScopeKey] = N1RequestScope.Begin(_efOptions);
    }

    public void OnPerformed(PerformedContext filterContext)
    {
        if (!_hangfireOptions.Capture) return;

        N1RequestScope? scope = null;
        if (filterContext.Items.TryGetValue(ScopeKey, out var scopeObj))
            scope = scopeObj as N1RequestScope;

        try
        {
            var backgroundJob = filterContext.BackgroundJob;
            var job = backgroundJob.Job;

            if (IsIgnoredType(job.Type)) return;

            var startTimestamp = filterContext.Items.TryGetValue(StartKey, out var ts)
                ? (long)ts : Stopwatch.GetTimestamp();
            var durationMs = ElapsedMs(startTimestamp);

            var enqueueRequestId = filterContext.Items.TryGetValue(EnqueueRequestIdKey, out var rid)
                ? rid as string : null;
            if (string.IsNullOrEmpty(enqueueRequestId)) enqueueRequestId = null;

            scope?.Complete(_recorder);

            var succeeded = filterContext.Exception is null || filterContext.ExceptionHandled;
            var state = succeeded ? "Succeeded" : "Failed";
            string? errorType = null;
            string? errorMessage = null;
            if (!succeeded && filterContext.Exception is not null)
            {
                errorType = filterContext.Exception.GetType().FullName;
                errorMessage = filterContext.Exception.Message;
            }

            var fullTypeName = job.Type.FullName;
            var content = new JobExecutionEntryContent(
                JobId: backgroundJob.Id,
                JobType: fullTypeName,
                MethodName: job.Method.Name,
                EnqueueRequestId: enqueueRequestId,
                State: state,
                ErrorType: errorType,
                ErrorMessage: errorMessage);

            var tags = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["job.id"] = backgroundJob.Id,
                ["job.method"] = job.Method.Name,
                ["job.state"] = state,
                ["job.execute"] = "true",
            };
            if (fullTypeName is not null)
                tags["job.type"] = fullTypeName;
            if (enqueueRequestId is not null)
                tags["job.enqueue.request-id"] = enqueueRequestId;

            var entry = new LookoutEntry(
                Id: Guid.NewGuid(),
                Type: "job-execution",
                Timestamp: DateTimeOffset.UtcNow,
                RequestId: enqueueRequestId,
                DurationMs: durationMs,
                Tags: tags,
                Content: JsonSerializer.Serialize(content, LookoutJson.Options));

            _recorder.Record(entry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lookout failed to capture Hangfire job execution.");
        }
        finally
        {
            scope?.Dispose();
        }
    }

    private bool IsIgnoredType(Type type)
    {
        if (_hangfireOptions.IgnoreJobTypes.Count == 0) return false;

        var t = type;
        while (t is not null)
        {
            if (_hangfireOptions.IgnoreJobTypes.Contains(t.FullName ?? t.Name))
                return true;
            t = t.BaseType;
        }
        return false;
    }

    private static double ElapsedMs(long startTimestamp)
    {
        var ticks = Stopwatch.GetTimestamp() - startTimestamp;
        return ticks * 1000.0 / Stopwatch.Frequency;
    }
}
