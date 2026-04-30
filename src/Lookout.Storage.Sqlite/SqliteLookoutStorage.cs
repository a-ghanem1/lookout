using System.Text;
using System.Text.Json;
using Lookout.Core;
using Microsoft.Data.Sqlite;

namespace Lookout.Storage.Sqlite;

public sealed class SqliteLookoutStorage : ILookoutStorage, IDisposable
{
    private const int MaxLimit = 200;

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

    public async Task<int> PruneOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = "DELETE FROM entries WHERE timestamp_utc < @cutoff";
        cmd.Parameters.AddWithValue("@cutoff", cutoff.ToUnixTimeMilliseconds());
        var deleted = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return deleted;
    }

    public async Task<int> PruneToMaxCountAsync(int max, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using var countCmd = conn.CreateCommand();
        countCmd.Transaction = (SqliteTransaction)tx;
        countCmd.CommandText = "SELECT COUNT(*) FROM entries";
        var total = (long)(await countCmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;

        var deleted = 0;
        if (total > max)
        {
            await using var deleteCmd = conn.CreateCommand();
            deleteCmd.Transaction = (SqliteTransaction)tx;
            deleteCmd.CommandText =
                "DELETE FROM entries WHERE id IN " +
                "(SELECT id FROM entries ORDER BY timestamp_utc ASC LIMIT @limit)";
            deleteCmd.Parameters.AddWithValue("@limit", total - max);
            deleted = await deleteCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
        return deleted;
    }

    public async Task<IReadOnlyList<LookoutEntry>> QueryAsync(LookoutQuery query, CancellationToken ct = default)
    {
        var limit = Math.Clamp(query.Limit, 1, MaxLimit);

        var sql = new StringBuilder();
        sql.Append("SELECT e.id, e.type, e.timestamp_utc, e.request_id, e.duration_ms, e.tags_json, e.content_json ");
        sql.Append("FROM entries e ");

        var where = new List<string>();

        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            where.Add("e.rowid IN (SELECT rowid FROM entries_fts WHERE entries_fts MATCH @q)");
            cmd.Parameters.AddWithValue("@q", query.Q);
        }

        if (query.TypeIn is { Count: > 0 } typeIn)
        {
            if (typeIn.Count == 1)
            {
                where.Add("e.type = @type");
                cmd.Parameters.AddWithValue("@type", typeIn[0]);
            }
            else
            {
                var pnames = Enumerable.Range(0, typeIn.Count).Select(i => $"@t{i}").ToArray();
                where.Add($"e.type IN ({string.Join(",", pnames)})");
                for (var ti = 0; ti < typeIn.Count; ti++)
                    cmd.Parameters.AddWithValue($"@t{ti}", typeIn[ti]);
            }
        }
        else if (!string.IsNullOrEmpty(query.Type))
        {
            where.Add("e.type = @type");
            cmd.Parameters.AddWithValue("@type", query.Type);
        }

        if (!string.IsNullOrEmpty(query.Method))
        {
            where.Add("json_extract(e.tags_json, '$.\"http.method\"') = @method");
            cmd.Parameters.AddWithValue("@method", query.Method);
        }

        if (query.StatusMin is int min)
        {
            where.Add("CAST(json_extract(e.tags_json, '$.\"http.status\"') AS INTEGER) >= @statusMin");
            cmd.Parameters.AddWithValue("@statusMin", min);
        }

        if (query.StatusMax is int max)
        {
            where.Add("CAST(json_extract(e.tags_json, '$.\"http.status\"') AS INTEGER) <= @statusMax");
            cmd.Parameters.AddWithValue("@statusMax", max);
        }

        if (!string.IsNullOrEmpty(query.Path))
        {
            where.Add("LOWER(json_extract(e.tags_json, '$.\"http.path\"')) LIKE @path");
            cmd.Parameters.AddWithValue("@path", "%" + query.Path.ToLowerInvariant() + "%");
        }

        if (query.BeforeUnixMs is long before)
        {
            where.Add("e.timestamp_utc < @before");
            cmd.Parameters.AddWithValue("@before", before);
        }

        if (query.MinDurationMs is double minDur)
        {
            where.Add("e.duration_ms >= @minDur");
            cmd.Parameters.AddWithValue("@minDur", minDur);
        }

        if (query.MaxDurationMs is double maxDur)
        {
            where.Add("e.duration_ms <= @maxDur");
            cmd.Parameters.AddWithValue("@maxDur", maxDur);
        }

        if (!string.IsNullOrEmpty(query.UrlHost))
        {
            where.Add("LOWER(json_extract(e.tags_json, '$.\"http.url.host\"')) LIKE @urlHost");
            cmd.Parameters.AddWithValue("@urlHost", "%" + query.UrlHost.ToLowerInvariant() + "%");
        }

        if (query.ErrorsOnly is true)
        {
            where.Add("json_extract(e.tags_json, '$.\"http.error\"') IS NOT NULL");
        }

        if (where.Count > 0)
        {
            sql.Append("WHERE ");
            sql.Append(string.Join(" AND ", where));
            sql.Append(' ');
        }

        var orderBy = query.Sort == "duration"
            ? "ORDER BY COALESCE(e.duration_ms, 0) DESC, e.id DESC"
            : "ORDER BY e.timestamp_utc DESC, e.id DESC";
        sql.Append(orderBy);
        sql.Append(" LIMIT @limit");
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.CommandText = sql.ToString();

        var results = new List<LookoutEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(ReadEntry(reader));

        return results;
    }

    public async Task<LookoutEntry?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, type, timestamp_utc, request_id, duration_ms, tags_json, content_json " +
            "FROM entries WHERE id = @id LIMIT 1";
        cmd.Parameters.AddWithValue("@id", id.ToString());

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadEntry(reader) : null;
    }

    public async Task<IReadOnlyList<LookoutEntry>> GetByRequestIdAsync(string requestId, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, type, timestamp_utc, request_id, duration_ms, tags_json, content_json " +
            "FROM entries WHERE request_id = @rid ORDER BY timestamp_utc ASC, id ASC";
        cmd.Parameters.AddWithValue("@rid", requestId);

        var results = new List<LookoutEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(ReadEntry(reader));
        return results;
    }

    public async Task<(long Requests, long Queries, long Exceptions, long Logs, long Cache, long HttpClients, long Jobs, long Dump)> GetCountsAsync(CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT " +
            "SUM(CASE WHEN type = 'http' THEN 1 ELSE 0 END)," +
            "SUM(CASE WHEN type IN ('ef','sql') THEN 1 ELSE 0 END)," +
            "SUM(CASE WHEN type = 'exception' THEN 1 ELSE 0 END)," +
            "SUM(CASE WHEN type = 'log' THEN 1 ELSE 0 END)," +
            "SUM(CASE WHEN type = 'cache' THEN 1 ELSE 0 END)," +
            "SUM(CASE WHEN type = 'http-out' THEN 1 ELSE 0 END)," +
            "SUM(CASE WHEN type IN ('job-enqueue','job-execution') THEN 1 ELSE 0 END)," +
            "SUM(CASE WHEN type = 'dump' THEN 1 ELSE 0 END) " +
            "FROM entries";

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return (0, 0, 0, 0, 0, 0, 0, 0);

        return (
            reader.IsDBNull(0) ? 0 : reader.GetInt64(0),
            reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
            reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
            reader.IsDBNull(3) ? 0 : reader.GetInt64(3),
            reader.IsDBNull(4) ? 0 : reader.GetInt64(4),
            reader.IsDBNull(5) ? 0 : reader.GetInt64(5),
            reader.IsDBNull(6) ? 0 : reader.GetInt64(6),
            reader.IsDBNull(7) ? 0 : reader.GetInt64(7));
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
