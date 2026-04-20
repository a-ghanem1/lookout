using Microsoft.Data.Sqlite;

namespace Lookout.Storage.Sqlite;

internal interface ISqliteConnectionFactory
{
    Task<SqliteConnection> OpenAsync(CancellationToken ct = default);
}
