using System.Text.Json.Serialization;

namespace Lookout.Core.Schemas;

/// <summary>JSON shape stored in <see cref="LookoutEntry.Content"/> for entries of type <c>ef</c>.</summary>
/// <param name="CommandText">The SQL command text.</param>
/// <param name="Parameters">Captured command parameters (redaction already applied).</param>
/// <param name="DurationMs">Query execution duration in milliseconds.</param>
/// <param name="RowsAffected">Rows affected, or <c>null</c> when not available.</param>
/// <param name="DbContextType">Fully-qualified <c>DbContext</c> type name, or <c>null</c>.</param>
/// <param name="CommandType">The kind of command executed.</param>
/// <param name="Stack">User-code stack frames at the time of execution.</param>
public sealed record EfEntryContent(
    string CommandText,
    IReadOnlyList<EfParameter> Parameters,
    double DurationMs,
    int? RowsAffected,
    string? DbContextType,
    EfCommandType CommandType,
    IReadOnlyList<EfStackFrame> Stack);

/// <summary>A captured EF Core command parameter.</summary>
/// <param name="Name">Parameter name.</param>
/// <param name="Value">Parameter value (redaction applied), or <c>null</c> when types-only capture is on.</param>
/// <param name="DbType">Database type name, or <c>null</c> when not available.</param>
public sealed record EfParameter(string Name, string? Value, string? DbType);

/// <summary>The kind of database command executed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EfCommandType
{
    Query,
    NonQuery,
    Reader,
    Scalar,
}

/// <summary>A single user-code frame from a stack trace capture.</summary>
/// <param name="Method">Fully-qualified method name.</param>
/// <param name="File">Source file path, or <c>null</c> when PDBs are unavailable.</param>
/// <param name="Line">Line number, or <c>null</c> when PDBs are unavailable.</param>
public sealed record EfStackFrame(string Method, string? File, int? Line);
