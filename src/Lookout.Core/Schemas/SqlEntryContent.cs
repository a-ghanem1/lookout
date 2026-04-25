namespace Lookout.Core.Schemas;

/// <summary>
/// JSON shape stored in <see cref="LookoutEntry.Content"/> for entries of type <c>sql</c>
/// (raw ADO.NET / Dapper — distinct from <c>ef</c> entries captured by the EF Core interceptor).
/// </summary>
/// <param name="CommandText">The SQL command text.</param>
/// <param name="Parameters">Captured command parameters (redaction already applied).</param>
/// <param name="DurationMs">Query execution duration in milliseconds.</param>
/// <param name="RowsAffected">Rows affected, or <c>null</c> when not available.</param>
/// <param name="CommandType">The <see cref="System.Data.CommandType"/> name, or <c>null</c>.</param>
/// <param name="Stack">User-code stack frames at the time of execution.</param>
public sealed record SqlEntryContent(
    string CommandText,
    IReadOnlyList<EfParameter> Parameters,
    double DurationMs,
    int? RowsAffected,
    string? CommandType,
    IReadOnlyList<EfStackFrame> Stack);
