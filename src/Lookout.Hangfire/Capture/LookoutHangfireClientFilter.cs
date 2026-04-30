using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Hangfire.Client;
using Hangfire.States;
using Lookout.Core;
using Lookout.Core.Diagnostics;
using Lookout.Core.Schemas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lookout.Hangfire;

/// <summary>
/// Enqueue-side Hangfire filter. Records a <c>job-enqueue</c> entry for every
/// <c>BackgroundJob.Enqueue(...)</c> call (and its scheduled / batched cousins) that flows
/// through the Hangfire client pipeline. Stamps the originating request id as a job parameter
/// so the server-side filter can cross-link executions back to their enqueue request.
/// </summary>
internal sealed class LookoutHangfireClientFilter : IClientFilter
{
    private const string TruncationMarker = "…[lookout:truncated]";
    private const string StartKey = "lookout.start";
    internal const string EnqueueRequestIdParam = "lookout.enqueue.request-id";

    private readonly ILookoutRecorder _recorder;
    private readonly HangfireOptions _hangfireOptions;
    private readonly RedactionOptions _redaction;
    private readonly ILogger<LookoutHangfireClientFilter> _logger;

    public LookoutHangfireClientFilter(
        ILookoutRecorder recorder,
        IOptions<LookoutOptions> options,
        ILogger<LookoutHangfireClientFilter> logger)
    {
        _recorder = recorder;
        _hangfireOptions = options.Value.Hangfire;
        _redaction = options.Value.Redaction;
        _logger = logger;
    }

    public void OnCreating(CreatingContext filterContext)
    {
        if (!_hangfireOptions.Capture) return;

        filterContext.Items[StartKey] = Stopwatch.GetTimestamp();

        var requestId = Activity.Current?.RootId;
        if (requestId is not null)
            filterContext.SetJobParameter(EnqueueRequestIdParam, requestId);
    }

    public void OnCreated(CreatedContext filterContext)
    {
        if (!_hangfireOptions.Capture) return;

        try
        {
            var backgroundJob = filterContext.BackgroundJob;
            if (backgroundJob is null) return;

            var job = backgroundJob.Job;

            var fullTypeName = job.Type.FullName;
            if (IsIgnoredType(job.Type)) return;

            var startTimestamp = filterContext.Items.TryGetValue(StartKey, out var ts)
                ? (long)ts
                : Stopwatch.GetTimestamp();
            var durationMs = ElapsedMs(startTimestamp);

            var requestId = Activity.Current?.RootId;

            var arguments = BuildArguments(job);
            var queue = (filterContext.InitialState as EnqueuedState)?.Queue ?? "default";
            var state = filterContext.InitialState?.Name ?? "Enqueued";

            string? errorType = null;
            string? errorMessage = null;
            if (filterContext.Exception is not null)
            {
                errorType = filterContext.Exception.GetType().FullName;
                errorMessage = filterContext.Exception.Message;
            }

            var content = new JobEnqueueEntryContent(
                JobId: backgroundJob.Id,
                JobType: fullTypeName,
                MethodName: job.Method.Name,
                Arguments: arguments,
                Queue: queue,
                State: state,
                ErrorType: errorType,
                ErrorMessage: errorMessage);

            var tags = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["job.id"] = backgroundJob.Id,
                ["job.method"] = job.Method.Name,
                ["job.queue"] = queue,
                ["job.state"] = state,
                ["job.enqueue"] = "true",
            };
            if (fullTypeName is not null)
                tags["job.type"] = fullTypeName;

            var entry = new LookoutEntry(
                Id: Guid.NewGuid(),
                Type: "job-enqueue",
                Timestamp: DateTimeOffset.UtcNow,
                RequestId: requestId,
                DurationMs: durationMs,
                Tags: tags,
                Content: JsonSerializer.Serialize(content, LookoutJson.Options));

            _recorder.Record(entry);
            N1RequestScope.Current?.TrackJobEnqueue();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lookout failed to capture Hangfire job enqueue.");
        }
        // filterContext.Exception is not set to ExceptionHandled=true, so Hangfire
        // still propagates any original exception from job creation.
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

    private IReadOnlyList<JobArgument> BuildArguments(global::Hangfire.Common.Job job)
    {
        if (!_hangfireOptions.CaptureArguments) return Array.Empty<JobArgument>();

        var parameters = job.Method.GetParameters();
        var args = job.Args;
        var count = Math.Min(parameters.Length, args.Count);
        var result = new List<JobArgument>(count);

        for (var i = 0; i < count; i++)
        {
            var param = parameters[i];
            var value = args[i];
            var typeName = param.ParameterType.FullName ?? param.ParameterType.Name;
            var name = param.Name ?? $"arg{i.ToString(CultureInfo.InvariantCulture)}";

            string rendered;
            try
            {
                rendered = JsonSerializer.Serialize(value, LookoutJson.Options);
            }
            catch
            {
                rendered = value?.ToString() ?? "null";
            }

            if (IsSensitiveParam(name))
            {
                rendered = "***";
            }
            else
            {
                var byteCount = Encoding.UTF8.GetByteCount(rendered);
                if (byteCount > _hangfireOptions.ArgumentMaxBytes)
                {
                    var bytes = Encoding.UTF8.GetBytes(rendered);
                    rendered = Encoding.UTF8.GetString(bytes, 0, _hangfireOptions.ArgumentMaxBytes)
                               + TruncationMarker;
                }
            }

            result.Add(new JobArgument(name, typeName, rendered));
        }

        return result;
    }

    private bool IsSensitiveParam(string name) =>
        _redaction.QueryParams.Contains(name) || _redaction.FormFields.Contains(name);

    private static double ElapsedMs(long startTimestamp)
    {
        var ticks = Stopwatch.GetTimestamp() - startTimestamp;
        return ticks * 1000.0 / Stopwatch.Frequency;
    }
}
