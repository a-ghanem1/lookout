using System.Text.Json;
using Lookout.Core;
using Microsoft.Data.Sqlite;

namespace Lookout.Storage.Sqlite;

public sealed class SqliteLookoutStorage : ILookoutStorage, IDisposable
{
    private readonly ISqliteConnectionFactory _factory;

    public SqliteLookoutStorage(LookoutOptions options)
        : this(new SqliteConnectionFactory(options)) { }

    internal SqliteLookoutStorage(ISqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task WriteAsync(IReadOnlyList<LookoutEntry> entries, CancellationToken ct = default)
    {
        if (entries.Count == 0) return;

        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        foreach (var entry in entries)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText =
                "INSERT OR IGNORE INTO entries (id, type, timestamp_utc, request_id, duration_ms, tags_json, content_json) " +
                "VALUES (@id, @type, @ts, @rid, @dur, @tags, @content)";
            cmd.Parameters.AddWithValue("@id", entry.Id.ToString());
            cmd.Parameters.AddWithValue("@type", entry.Type);
            cmd.Parameters.AddWithValue("@ts", entry.Timestamp.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("@rid", (object?)entry.RequestId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@dur", (object?)entry.DurationMs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@tags", JsonSerializer.Serialize(entry.Tags));
            cmd.Parameters.AddWithValue("@content", entry.Content);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LookoutEntry>> ReadRecentAsync(int limit, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, type, timestamp_utc, request_id, duration_ms, tags_json, content_json " +
            "FROM entries ORDER BY timestamp_utc DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<LookoutEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(ReadEntry(reader));

        return results;
    }

    public async Task<IReadOnlyList<LookoutEntry>> SearchAsync(string query, int limit, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT e.id, e.type, e.timestamp_utc, e.request_id, e.duration_ms, e.tags_json, e.content_json " +
            "FROM entries e " +
            "WHERE e.rowid IN (SELECT rowid FROM entries_fts WHERE entries_fts MATCH @query) " +
            "ORDER BY e.timestamp_utc DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@query", query);
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<LookoutEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(ReadEntry(reader));

        return results;
    }

    public void Dispose() => (_factory as IDisposable)?.Dispose();

    private static LookoutEntry ReadEntry(SqliteDataReader reader) =>
        new(
            Id: Guid.Parse(reader.GetString(0)),
            Type: reader.GetString(1),
            Timestamp: DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2)),
            RequestId: reader.IsDBNull(3) ? null : reader.GetString(3),
            DurationMs: reader.IsDBNull(4) ? null : reader.GetDouble(4),
            Tags: JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(5))
                  ?? new Dictionary<string, string>(),
            Content: reader.GetString(6));
}
