using System.Diagnostics;
using System.Text.Json;
using Lookout.Core;
using Lookout.Core.Capture;
using Lookout.Core.Diagnostics;
using Lookout.Core.Schemas;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using N1Scope = Lookout.Core.Diagnostics.N1RequestScope;

namespace Lookout.AspNetCore.Capture;

/// <summary>
/// Subscribes to Npgsql's <see cref="ActivitySource"/> (name: <c>"Npgsql"</c>) to capture raw
/// PostgreSQL command executions that bypass the EF Core interceptor (e.g. Dapper, raw
/// <c>NpgsqlCommand</c>).
/// </summary>
/// <remarks>
/// <para>
/// When <c>Lookout.EntityFrameworkCore</c> is installed and <c>AddEntityFrameworkCore()</c> has
/// been called, <see cref="EfCommandRegistry.EfInterceptorRegistered"/> is <c>true</c> and this
/// subscriber does nothing — the EF Core interceptor captures richer metadata (stack traces,
/// DbContext type, N+1 detection) for EF-originated queries. Raw non-EF Npgsql commands in the
/// same app are not captured in that configuration; this is a known v1 limitation.
/// </para>
/// <para>
/// The SQL text is read from the <c>db.query.text</c> activity tag that Npgsql sets on every
/// command activity. Activities without this tag (e.g. connection-open spans) are skipped.
/// </para>
/// </remarks>
public sealed class NpgsqlActivitySubscriber : IHostedService
{
    private const string NpgsqlSourceName = "Npgsql";
    private const string SqlTextTag = "db.query.text";

    private readonly ILookoutRecorder _recorder;
    private readonly EfOptions _efOptions;
    private readonly RedactionOptions _redactionOptions;
    private ActivityListener? _listener;

    public NpgsqlActivitySubscriber(ILookoutRecorder recorder, IOptions<LookoutOptions> options)
    {
        _recorder = recorder;
        var opts = options.Value;
        _efOptions = opts.Ef;
        _redactionOptions = opts.Redaction;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // EF interceptor captures richer data — don't double-record.
        if (EfCommandRegistry.EfInterceptorRegistered)
            return Task.CompletedTask;

        _listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == NpgsqlSourceName,
            // AllDataAndRecorded ensures tags are populated on the activity.
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = OnActivityStopped,
        };
        ActivitySource.AddActivityListener(_listener);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _listener?.Dispose();
        _listener = null;
        return Task.CompletedTask;
    }

    private void OnActivityStopped(Activity activity)
    {
        // Only command activities carry db.query.text; skip connection-open etc.
        var sqlText = activity.GetTagItem(SqlTextTag) as string;
        if (string.IsNullOrEmpty(sqlText))
            return;

        var durationMs = activity.Duration.TotalMilliseconds;

        var content = new SqlEntryContent(
            CommandText: sqlText,
            Parameters: [],
            DurationMs: durationMs,
            RowsAffected: null,
            CommandType: "Text",
            Stack: []);

        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["db.system"] = "postgresql",
            ["db.provider"] = "Npgsql",
        };

        if (activity.Status == ActivityStatusCode.Error)
            tags["error"] = activity.StatusDescription ?? "true";

        // activity.RootId is the root of the W3C trace — the same value that the HTTP
        // middleware stores as RequestId, so this correlates the query to its request.
        var entry = new LookoutEntry(
            Id: Guid.NewGuid(),
            Type: "sql",
            Timestamp: DateTimeOffset.UtcNow,
            RequestId: activity.RootId,
            DurationMs: durationMs,
            Tags: tags,
            Content: JsonSerializer.Serialize(content, LookoutJson.Options));

        var scope = N1Scope.Current;
        if (scope is not null)
            scope.Track(entry, sqlText);
        else
            _recorder.Record(entry);
    }
}
