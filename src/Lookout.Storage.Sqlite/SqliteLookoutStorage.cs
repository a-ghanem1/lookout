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

        if (!string.IsNullOrEmpty(query.MinLevel) && TryGetLogLevelOrdinal(query.MinLevel, out var levelOrd))
        {
            where.Add(
                "CASE json_extract(e.tags_json, '$.\"log.level\"') " +
                "WHEN 'Trace' THEN 0 WHEN 'Debug' THEN 1 WHEN 'Information' THEN 2 " +
                "WHEN 'Warning' THEN 3 WHEN 'Error' THEN 4 WHEN 'Critical' THEN 5 ELSE -1 END >= @minLevel");
            cmd.Parameters.AddWithValue("@minLevel", levelOrd);
        }

        if (query.Handled is bool handled)
        {
            where.Add("json_extract(e.tags_json, '$.\"exception.handled\"') = @handled");
            cmd.Parameters.AddWithValue("@handled", handled ? "true" : "false");
        }

        if (query.Tags is { Count: > 0 } tags)
        {
            for (var i = 0; i < tags.Count; i++)
            {
                var (tagKey, tagValue) = tags[i];
                var pKey = $"@tagVal{i}";
                where.Add($"json_extract(e.tags_json, '$.\"{tagKey}\"') = {pKey}");
                cmd.Parameters.AddWithValue(pKey, tagValue);
            }
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

    public async Task<(long Hits, long Misses, long Sets, long Removes)> GetCacheSummaryAsync(
        long? fromUnixMs = null, long? toUnixMs = null, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();

        var where = new List<string> { "type = 'cache'" };
        if (fromUnixMs is long from) { where.Add("timestamp_utc >= @from"); cmd.Parameters.AddWithValue("@from", from); }
        if (toUnixMs is long to) { where.Add("timestamp_utc <= @to"); cmd.Parameters.AddWithValue("@to", to); }
        var whereClause = $"WHERE {string.Join(" AND ", where)}";

        cmd.CommandText =
            "SELECT " +
            "SUM(CASE WHEN json_extract(tags_json, '$.\"cache.hit\"') = 'true' THEN 1 ELSE 0 END)," +
            "SUM(CASE WHEN json_extract(tags_json, '$.\"cache.hit\"') = 'false' THEN 1 ELSE 0 END)," +
            "SUM(CASE WHEN json_extract(content_json, '$.operation') = 'Set' THEN 1 ELSE 0 END)," +
            "SUM(CASE WHEN json_extract(content_json, '$.operation') = 'Remove' THEN 1 ELSE 0 END) " +
            $"FROM entries {whereClause}";

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return (0, 0, 0, 0);

        return (
            reader.IsDBNull(0) ? 0 : reader.GetInt64(0),
            reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
            reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
            reader.IsDBNull(3) ? 0 : reader.GetInt64(3));
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

    public async Task<int> DeleteAllAsync(CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = "DELETE FROM entries";
        var deleted = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return deleted;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchWithSnippetAsync(
        string query, int limit, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();

        // The FTS table is contentless (content=''), so snippet() always returns "".
        // Instead, join back to entries and read content_json to build a meaningful summary.
        cmd.CommandText =
            "SELECT e.id, e.type, e.timestamp_utc, e.request_id, e.content_json " +
            "FROM entries e " +
            "JOIN entries_fts ON entries_fts.rowid = e.rowid " +
            "WHERE entries_fts MATCH @q " +
            "ORDER BY entries_fts.rank " +
            "LIMIT @limit";
        cmd.Parameters.AddWithValue("@q", query);
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<SearchResult>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var type = reader.GetString(1);
            var contentJson = reader.IsDBNull(4) ? "{}" : reader.GetString(4);
            results.Add(new SearchResult(
                Id: Guid.Parse(reader.GetString(0)),
                Type: type,
                Timestamp: DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2)),
                RequestId: reader.IsDBNull(3) ? null : reader.GetString(3),
                Snippet: BuildSummary(type, contentJson, query)));
        }
        return results;
    }

    private static string BuildSummary(string type, string contentJson, string query)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(contentJson);
            var root = doc.RootElement;
            var raw = type switch
            {
                "http"     => GetStr(root, "method") + " " + GetStr(root, "path"),
                "ef"       => FirstLine(GetStr(root, "commandText")),
                "sql"      => FirstLine(GetStr(root, "commandText")),
                "exception"=> GetStr(root, "exceptionType") + ": " + GetStr(root, "message"),
                "log"      => GetStr(root, "message"),
                "cache"    => GetStr(root, "operation") + " " + GetStr(root, "key"),
                "http-out" => GetStr(root, "method") + " " + GetStr(root, "url"),
                "job-enqueue" or "job-execution"
                           => GetStr(root, "methodName") + " → " + GetStr(root, "state"),
                "dump"     => GetStr(root, "label", GetStr(root, "valueType", "dump")),
                _          => "",
            };
            if (raw.Length > 120) raw = raw[..117] + "…";
            return HighlightFirstTerm(raw, query);
        }
        catch { return ""; }
    }

    private static string GetStr(System.Text.Json.JsonElement el, string prop, string fallback = "")
    {
        if (el.TryGetProperty(prop, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String)
            return v.GetString() ?? fallback;
        return fallback;
    }

    private static string FirstLine(string sql)
    {
        var nl = sql.IndexOfAny(['\n', '\r']);
        if (nl > 0) sql = sql[..nl].TrimEnd();
        return sql.Length > 100 ? sql[..97] + "…" : sql;
    }

    private static string HighlightFirstTerm(string text, string query)
    {
        // Pull the first bare word from the FTS query (strip *, quotes, boolean ops).
        var term = System.Text.RegularExpressions.Regex
            .Match(query, @"[A-Za-z0-9_\-]{2,}").Value;
        if (string.IsNullOrEmpty(term)) return text;
        var idx = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return text;
        return text[..idx] + "§" + text[idx..(idx + term.Length)] + "§" + text[(idx + term.Length)..];
    }

    public async Task<IReadOnlyList<LogHistogramBucket>> GetLogHistogramAsync(
        int bucketCount, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);

        await using var rangeCmd = conn.CreateCommand();
        rangeCmd.CommandText =
            "SELECT MIN(timestamp_utc), MAX(timestamp_utc) FROM entries WHERE type = 'log'";
        await using var rangeReader = await rangeCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rangeReader.ReadAsync(ct).ConfigureAwait(false) || rangeReader.IsDBNull(0))
            return Array.Empty<LogHistogramBucket>();

        var minTs = rangeReader.GetInt64(0);
        var maxTs = rangeReader.GetInt64(1);
        await rangeReader.CloseAsync().ConfigureAwait(false);

        if (minTs == maxTs) maxTs = minTs + 1;
        var bucketMs = (maxTs - minTs + bucketCount - 1) / bucketCount;
        if (bucketMs < 1) bucketMs = 1;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT " +
            "  (timestamp_utc - @minTs) / @bucketMs AS bucket, " +
            "  json_extract(tags_json, '$.\"log.level\"') AS level, " +
            "  COUNT(*) AS cnt " +
            "FROM entries WHERE type = 'log' " +
            "GROUP BY bucket, level";
        cmd.Parameters.AddWithValue("@minTs", minTs);
        cmd.Parameters.AddWithValue("@bucketMs", bucketMs);

        var bucketMap = new Dictionary<long, LogHistogramBucket>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var b = reader.GetInt64(0);
            var level = reader.IsDBNull(1) ? "Information" : reader.GetString(1);
            var cnt = reader.GetInt64(2);
            if (!bucketMap.TryGetValue(b, out var bucket))
            {
                var from = minTs + b * bucketMs;
                bucketMap[b] = bucket = new LogHistogramBucket(
                    From: from, To: from + bucketMs,
                    Trace: 0, Debug: 0, Information: 0, Warning: 0, Error: 0, Critical: 0);
            }
            bucketMap[b] = level switch
            {
                "Trace" => bucket with { Trace = bucket.Trace + cnt },
                "Debug" => bucket with { Debug = bucket.Debug + cnt },
                "Information" => bucket with { Information = bucket.Information + cnt },
                "Warning" => bucket with { Warning = bucket.Warning + cnt },
                "Error" => bucket with { Error = bucket.Error + cnt },
                "Critical" => bucket with { Critical = bucket.Critical + cnt },
                _ => bucket with { Information = bucket.Information + cnt },
            };
        }
        return bucketMap.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
    }

    public async Task<IReadOnlyList<CacheKeyStats>> GetCacheByKeyAsync(
        int limit, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT " +
            "  json_extract(tags_json, '$.\"cache.key\"') AS ckey, " +
            "  SUM(CASE WHEN json_extract(tags_json, '$.\"cache.hit\"') = 'true' THEN 1 ELSE 0 END) AS hits, " +
            "  SUM(CASE WHEN json_extract(tags_json, '$.\"cache.hit\"') = 'false' THEN 1 ELSE 0 END) AS misses, " +
            "  SUM(CASE WHEN json_extract(content_json, '$.operation') = 'Set' THEN 1 ELSE 0 END) AS sets " +
            "FROM entries WHERE type = 'cache' AND ckey IS NOT NULL " +
            "GROUP BY ckey " +
            "ORDER BY (hits + misses + sets) DESC " +
            "LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<CacheKeyStats>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var hits = reader.GetInt64(1);
            var misses = reader.GetInt64(2);
            var sets = reader.GetInt64(3);
            var total = hits + misses;
            var hitRatio = total > 0 ? (double)hits / total : 0.0;
            results.Add(new CacheKeyStats(
                Key: reader.GetString(0),
                Hits: hits,
                Misses: misses,
                Sets: sets,
                HitRatio: hitRatio));
        }
        return results;
    }

    public async Task<long> GetTotalCountAsync(CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM entries";
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public void Dispose() => (_factory as IDisposable)?.Dispose();

    private static bool TryGetLogLevelOrdinal(string level, out int ordinal)
    {
        ordinal = level switch
        {
            "Trace" => 0,
            "Debug" => 1,
            "Information" => 2,
            "Warning" => 3,
            "Error" => 4,
            "Critical" => 5,
            _ => -1,
        };
        return ordinal >= 0;
    }

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
