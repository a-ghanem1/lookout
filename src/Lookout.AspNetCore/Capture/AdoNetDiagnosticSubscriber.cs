using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;
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
/// Subscribes to <see cref="DiagnosticListener"/> sources to capture raw ADO.NET / Dapper
/// <see cref="DbCommand"/> executions that bypass the EF Core interceptor.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a singleton <see cref="IHostedService"/> via <c>AddLookout()</c>.
/// <see cref="StartAsync"/> subscribes to <see cref="DiagnosticListener.AllListeners"/>;
/// <see cref="StopAsync"/> disposes all subscriptions.
/// </para>
/// <para>
/// De-dupe rule: commands already claimed by <see cref="LookoutDbCommandInterceptor"/> are
/// skipped via <see cref="EfCommandRegistry"/>. EF wins because it captures richer metadata
/// (DbContext type, strongly-typed CommandType).
/// </para>
/// <para>
/// Listens to <c>SqlClientDiagnosticListener</c> (both <c>Microsoft.Data.SqlClient</c> and
/// <c>System.Data.SqlClient</c> publish under this name). Other providers can be added to
/// <see cref="WatchedSources"/> as needed.
/// </para>
/// </remarks>
public sealed class AdoNetDiagnosticSubscriber : IHostedService, IObserver<DiagnosticListener>
{
    private readonly ILookoutRecorder _recorder;
    private readonly EfOptions _efOptions;
    private readonly RedactionOptions _redactionOptions;

    // Keyed by DbCommand; value is the Stopwatch timestamp when execution started.
    private readonly ConditionalWeakTable<DbCommand, StrongBox<long>> _startTimes = new();

    private IDisposable? _allListenersSubscription;
    private readonly List<IDisposable> _sourceSubscriptions = [];

    /// <summary>
    /// DiagnosticListener source names monitored by this subscriber.
    /// Both Microsoft.Data.SqlClient and System.Data.SqlClient publish under
    /// <c>SqlClientDiagnosticListener</c>.
    /// </summary>
    internal static readonly HashSet<string> WatchedSources =
        new(StringComparer.OrdinalIgnoreCase) { "SqlClientDiagnosticListener" };

    // Event names fired before command execution (to capture the start timestamp).
    private static readonly HashSet<string> CommandBeforeEvents =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft.Data.SqlClient.WriteCommandBefore",
            "System.Data.SqlClient.WriteCommandBefore",
        };

    // Event names fired after command execution (to record the entry).
    private static readonly HashSet<string> CommandAfterEvents =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft.Data.SqlClient.WriteCommandAfter",
            "System.Data.SqlClient.WriteCommandAfter",
        };

    public AdoNetDiagnosticSubscriber(ILookoutRecorder recorder, IOptions<LookoutOptions> options)
    {
        _recorder = recorder;
        var opts = options.Value;
        _efOptions = opts.Ef;
        _redactionOptions = opts.Redaction;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(this);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_sourceSubscriptions)
        {
            foreach (var sub in _sourceSubscriptions)
                sub.Dispose();
            _sourceSubscriptions.Clear();
        }
        _allListenersSubscription?.Dispose();
        _allListenersSubscription = null;
        return Task.CompletedTask;
    }

    // IObserver<DiagnosticListener> — called when a new DiagnosticListener is published.
    void IObserver<DiagnosticListener>.OnNext(DiagnosticListener listener)
    {
        if (!WatchedSources.Contains(listener.Name)) return;
        var sub = listener.Subscribe(new CommandObserver(this));
        lock (_sourceSubscriptions)
            _sourceSubscriptions.Add(sub);
    }

    void IObserver<DiagnosticListener>.OnError(Exception error) { }
    void IObserver<DiagnosticListener>.OnCompleted() { }

    // ── Core recording ─────────────────────────────────────────────────────────

    /// <summary>
    /// Records a raw ADO.NET command as a <c>sql</c> <see cref="LookoutEntry"/>.
    /// No-op when <paramref name="command"/> was claimed by the EF Core interceptor
    /// (de-dupe: EF wins).
    /// </summary>
    internal void RecordAdoCommand(DbCommand command, double durationMs, int? rowsAffected)
    {
        if (EfCommandRegistry.IsMarked(command)) return;

        var provider = command.Connection?.GetType().Name;

        var content = new SqlEntryContent(
            CommandText: command.CommandText ?? string.Empty,
            Parameters: BuildParameters(command),
            DurationMs: durationMs,
            RowsAffected: rowsAffected,
            CommandType: command.CommandType.ToString(),
            Stack: StackTraceCapture.Capture(skipFrames: 0, _efOptions.MaxStackFrames));

        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["db.system"] = "ado",
        };
        if (provider is not null)
            tags["db.provider"] = provider;

        var entry = new LookoutEntry(
            Id: Guid.NewGuid(),
            Type: "sql",
            Timestamp: DateTimeOffset.UtcNow,
            RequestId: Activity.Current?.RootId,
            DurationMs: durationMs,
            Tags: tags,
            Content: JsonSerializer.Serialize(content, LookoutJson.Options));

        var scope = N1Scope.Current;
        if (scope is not null)
            scope.Track(entry, command.CommandText ?? string.Empty);
        else
            _recorder.Record(entry);
    }

    private IReadOnlyList<EfParameter> BuildParameters(DbCommand command)
    {
        var count = command.Parameters.Count;
        if (count == 0) return [];

        var result = new List<EfParameter>(count);
        foreach (DbParameter p in command.Parameters)
        {
            string? value = null;
            if (!_efOptions.CaptureParameterTypesOnly && _efOptions.CaptureParameterValues)
            {
                var raw = p.Value is null or DBNull ? null : p.Value.ToString();
                var nameForRedaction = p.ParameterName.TrimStart('@', ':', '?');
                value = RedactionPipeline.RedactSqlParameterValue(nameForRedaction, raw, _redactionOptions);
            }
            result.Add(new EfParameter(p.ParameterName, value, p.DbType.ToString()));
        }
        return result;
    }

    // ── DiagnosticListener event observer ──────────────────────────────────────

    private sealed class CommandObserver : IObserver<KeyValuePair<string, object?>>
    {
        private readonly AdoNetDiagnosticSubscriber _parent;

        public CommandObserver(AdoNetDiagnosticSubscriber parent) => _parent = parent;

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Value is null) return;

            if (CommandBeforeEvents.Contains(value.Key))
            {
                var command = ExtractCommand(value.Value);
                if (command is null) return;

                // Record start timestamp; idempotent if already present.
                _parent._startTimes.GetOrCreateValue(command).Value = Stopwatch.GetTimestamp();
                return;
            }

            if (CommandAfterEvents.Contains(value.Key))
            {
                var command = ExtractCommand(value.Value);
                if (command is null) return;

                double durationMs = 0;
                if (_parent._startTimes.TryGetValue(command, out var startBox))
                {
                    var elapsed = Stopwatch.GetTimestamp() - startBox.Value;
                    durationMs = elapsed * 1000.0 / Stopwatch.Frequency;
                }

                _parent.RecordAdoCommand(command, durationMs, rowsAffected: null);
            }
        }

        public void OnError(Exception error) { }
        public void OnCompleted() { }

        /// <summary>
        /// Extracts the <see cref="DbCommand"/> from a SqlClient diagnostic payload.
        /// Payload is an anonymous type with either <c>Command</c> (PascalCase, newer versions)
        /// or <c>command</c> (camelCase, legacy versions).
        /// </summary>
        private static DbCommand? ExtractCommand(object payload)
        {
            var type = payload.GetType();
            return (type.GetProperty("Command")?.GetValue(payload)
                 ?? type.GetProperty("command")?.GetValue(payload)) as DbCommand;
        }
    }
}
