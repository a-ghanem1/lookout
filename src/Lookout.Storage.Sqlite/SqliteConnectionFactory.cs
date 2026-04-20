using Lookout.Core;
using Microsoft.Data.Sqlite;

namespace Lookout.Storage.Sqlite;

internal sealed class SqliteConnectionFactory : ISqliteConnectionFactory, IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private volatile bool _initialized;

    public SqliteConnectionFactory(LookoutOptions options)
    {
        _dbPath = options.StoragePath;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString();
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken ct = default)
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await using (var walCmd = conn.CreateCommand())
        {
            walCmd.CommandText = "PRAGMA journal_mode = WAL;";
            await walCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await EnsureSchemaAsync(conn, ct).ConfigureAwait(false);
        return conn;
    }

    private async Task EnsureSchemaAsync(SqliteConnection conn, CancellationToken ct)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized) return;

            if (_dbPath != ":memory:")
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(_dbPath));
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
            }

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = LoadSchema();
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static string LoadSchema()
    {
        var assembly = typeof(SqliteConnectionFactory).Assembly;
        using var stream = assembly.GetManifestResourceStream("Lookout.Storage.Sqlite.Schema.entries.sql")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void Dispose() => _initLock.Dispose();
}
