using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Lookout.Core;
using Lookout.Core.Capture;
using Lookout.Core.Diagnostics;
using Lookout.Core.Schemas;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace Lookout.EntityFrameworkCore;

/// <summary>
/// EF Core <see cref="DbCommandInterceptor"/> that records a <see cref="LookoutEntry"/> of type
/// <c>ef</c> after every executed command (reader, non-query, and scalar variants).
/// Stack frames are left empty in this version; they are populated in M4.3.
/// </summary>
public sealed class LookoutDbCommandInterceptor : DbCommandInterceptor
{
    private readonly ILookoutRecorder _recorder;
    private readonly EfOptions _efOptions;
    private readonly RedactionOptions _redactionOptions;

    public LookoutDbCommandInterceptor(ILookoutRecorder recorder, IOptions<LookoutOptions> options)
    {
        _recorder = recorder;
        var opts = options.Value;
        _efOptions = opts.Ef;
        _redactionOptions = opts.Redaction;
    }

    // ── Executing (before) — stamp the command so the ADO.NET subscriber skips it ──

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        EfCommandRegistry.Mark(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        EfCommandRegistry.Mark(command);
        return new ValueTask<InterceptionResult<DbDataReader>>(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        EfCommandRegistry.Mark(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        EfCommandRegistry.Mark(command);
        return new ValueTask<InterceptionResult<int>>(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        EfCommandRegistry.Mark(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        EfCommandRegistry.Mark(command);
        return new ValueTask<InterceptionResult<object>>(result);
    }

    // ── Executed (after) — record the entry ───────────────────────────────────

    public override DbDataReader ReaderExecuted(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Record(command, eventData, EfCommandType.Reader, rowsAffected: null);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData, EfCommandType.Reader, rowsAffected: null);
        return new ValueTask<DbDataReader>(result);
    }

    public override int NonQueryExecuted(
        DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Record(command, eventData, EfCommandType.NonQuery, rowsAffected: result);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData, EfCommandType.NonQuery, rowsAffected: result);
        return new ValueTask<int>(result);
    }

    public override object? ScalarExecuted(
        DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Record(command, eventData, EfCommandType.Scalar, rowsAffected: null);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData, EfCommandType.Scalar, rowsAffected: null);
        return new ValueTask<object?>(result);
    }

    private void Record(
        DbCommand command,
        CommandExecutedEventData eventData,
        EfCommandType commandType,
        int? rowsAffected)
    {
        var dbContextType = eventData.Context?.GetType().FullName;
        var provider = command.Connection?.GetType().Name;

        var content = new EfEntryContent(
            CommandText: command.CommandText ?? string.Empty,
            Parameters: BuildParameters(command),
            DurationMs: eventData.Duration.TotalMilliseconds,
            RowsAffected: rowsAffected,
            DbContextType: dbContextType,
            CommandType: commandType,
            Stack: StackTraceCapture.Capture(skipFrames: 0, _efOptions.MaxStackFrames));

        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["db.system"] = "ef",
        };
        if (provider is not null)
            tags["db.provider"] = provider;
        if (dbContextType is not null)
            tags["db.context"] = dbContextType;
        if (rowsAffected.HasValue)
            tags["db.rows"] = rowsAffected.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var entry = new LookoutEntry(
            Id: Guid.NewGuid(),
            Type: "ef",
            Timestamp: DateTimeOffset.UtcNow,
            RequestId: Activity.Current?.RootId,
            DurationMs: eventData.Duration.TotalMilliseconds,
            Tags: tags,
            Content: JsonSerializer.Serialize(content, LookoutJson.Options));

        _recorder.Record(entry);
    }

    private IReadOnlyList<EfParameter> BuildParameters(DbCommand command)
    {
        var count = command.Parameters.Count;
        if (count == 0) return Array.Empty<EfParameter>();

        var result = new List<EfParameter>(count);
        foreach (DbParameter p in command.Parameters)
        {
            string? value = null;
            if (!_efOptions.CaptureParameterTypesOnly && _efOptions.CaptureParameterValues)
            {
                var raw = p.Value is null or DBNull ? null : p.Value.ToString();
                // Strip leading '@', ':', or '?' before matching against the sensitive-name set.
                var nameForRedaction = p.ParameterName.TrimStart('@', ':', '?');
                value = RedactionPipeline.RedactSqlParameterValue(nameForRedaction, raw, _redactionOptions);
            }
            result.Add(new EfParameter(p.ParameterName, value, p.DbType.ToString()));
        }
        return result;
    }
}
