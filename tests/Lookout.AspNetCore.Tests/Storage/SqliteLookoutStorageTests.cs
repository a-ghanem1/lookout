using FluentAssertions;
using Lookout.Core;
using Lookout.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lookout.AspNetCore.Tests.Storage;

public sealed class SqliteLookoutStorageTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteLookoutStorage _storage;

    public SqliteLookoutStorageTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"lookout_test_{Guid.NewGuid():N}.db");
        _storage = new SqliteLookoutStorage(new LookoutOptions { StoragePath = _dbPath });
    }

    public void Dispose()
    {
        _storage.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal");
        if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm");
    }

    [Fact]
    public async Task Schema_AppliesCleanlyToEmptyFile()
    {
        var result = await _storage.ReadRecentAsync(1);

        result.Should().BeEmpty();
        File.Exists(_dbPath).Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_ReadRecentAsync_RoundtripsAllFields()
    {
        var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var entry = new LookoutEntry(
            Id: Guid.NewGuid(),
            Type: "http",
            Timestamp: timestamp,
            RequestId: "req-abc-123",
            DurationMs: 42.75,
            Tags: new Dictionary<string, string> { ["status"] = "200", ["method"] = "GET" },
            Content: "{\"url\":\"https://example.com\"}");

        await _storage.WriteAsync([entry]);
        var results = await _storage.ReadRecentAsync(10);

        results.Should().ContainSingle();
        var r = results[0];
        r.Id.Should().Be(entry.Id);
        r.Type.Should().Be("http");
        r.Timestamp.Should().Be(timestamp);
        r.RequestId.Should().Be("req-abc-123");
        r.DurationMs.Should().BeApproximately(42.75, precision: 0.001);
        r.Tags.Should().BeEquivalentTo(new Dictionary<string, string> { ["status"] = "200", ["method"] = "GET" });
        r.Content.Should().Be("{\"url\":\"https://example.com\"}");
    }

    [Fact]
    public async Task SearchAsync_FtsMatchesTagValueAndContentSubstring()
    {
        var entry = new LookoutEntry(
            Id: Guid.NewGuid(),
            Type: "ef",
            Timestamp: DateTimeOffset.UtcNow,
            RequestId: null,
            DurationMs: null,
            Tags: new Dictionary<string, string> { ["entity"] = "CustomerOrder" },
            Content: "{\"sql\":\"SELECT * FROM Invoices\"}");

        await _storage.WriteAsync([entry]);

        var byTag = await _storage.SearchAsync("CustomerOrder", 10);
        byTag.Should().ContainSingle(e => e.Id == entry.Id);

        var byContent = await _storage.SearchAsync("Invoices", 10);
        byContent.Should().ContainSingle(e => e.Id == entry.Id);
    }

    [Fact]
    public async Task Schema_AppliesIdempotentlyOnReopen()
    {
        await _storage.ReadRecentAsync(1);

        using var storage2 = new SqliteLookoutStorage(new LookoutOptions { StoragePath = _dbPath });

        var act = async () => await storage2.ReadRecentAsync(1);
        await act.Should().NotThrowAsync();
    }
}
