using FluentAssertions;
using Lookout.Core;
using Lookout.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lookout.AspNetCore.Tests.Retention;

/// <summary>
/// Integration tests that call the storage prune methods directly against a real SQLite file.
/// </summary>
public sealed class LookoutRetentionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteLookoutStorage _storage;

    public LookoutRetentionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"lookout_retention_{Guid.NewGuid():N}.db");
        _storage = new SqliteLookoutStorage(new LookoutOptions { StoragePath = _dbPath });
    }

    public void Dispose()
    {
        _storage.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }

    // ── PruneOlderThanAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task PruneOlderThan_WithMaxAgeZero_RemovesAllOldEntries()
    {
        var past = DateTimeOffset.UtcNow.AddHours(-1);
        await SeedEntriesAsync(10, baseTimestamp: past);

        // cutoff = UtcNow → all entries seeded 1 hour ago are older
        var deleted = await _storage.PruneOlderThanAsync(DateTimeOffset.UtcNow);

        deleted.Should().Be(10);
        var remaining = await _storage.ReadRecentAsync(20);
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task PruneOlderThan_DoesNotRemoveEntriesNewerThanCutoff()
    {
        var past = DateTimeOffset.UtcNow.AddHours(-2);
        var recent = DateTimeOffset.UtcNow.AddMinutes(-5);
        await SeedEntriesAsync(5, baseTimestamp: past);
        await SeedEntriesAsync(3, baseTimestamp: recent);

        // cutoff = 1 hour ago → removes the 2-hour-old entries, keeps the 5-minute-old ones
        var cutoff = DateTimeOffset.UtcNow.AddHours(-1);
        var deleted = await _storage.PruneOlderThanAsync(cutoff);

        deleted.Should().Be(5);
        var remaining = await _storage.ReadRecentAsync(20);
        remaining.Should().HaveCount(3);
    }

    // ── PruneToMaxCountAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task PruneToMaxCount_WithMaxFive_KeepsNewestFiveEntries()
    {
        // Seed 10 entries with staggered timestamps so oldest/newest is deterministic.
        var entries = Enumerable.Range(0, 10)
            .Select(i => MakeEntry(
                timestamp: DateTimeOffset.UtcNow.AddMinutes(-10 + i), // 0 = oldest, 9 = newest
                content: $"{{\"index\":{i}}}"))
            .ToList();
        await _storage.WriteAsync(entries);

        var deleted = await _storage.PruneToMaxCountAsync(5);

        deleted.Should().Be(5);
        var remaining = await _storage.ReadRecentAsync(20);
        remaining.Should().HaveCount(5);

        // The 5 newest (index 5–9) must survive.
        var survivingContents = remaining.Select(e => e.Content).ToHashSet();
        for (var i = 5; i <= 9; i++)
            survivingContents.Should().Contain($"{{\"index\":{i}}}");
    }

    [Fact]
    public async Task PruneToMaxCount_WhenUnderCap_DeletesNothing()
    {
        await SeedEntriesAsync(3, DateTimeOffset.UtcNow.AddMinutes(-1));

        var deleted = await _storage.PruneToMaxCountAsync(10);

        deleted.Should().Be(0);
        var remaining = await _storage.ReadRecentAsync(20);
        remaining.Should().HaveCount(3);
    }

    // ── FTS consistency ──────────────────────────────────────────────────────

    [Fact]
    public async Task FtsIndex_IsConsistent_AfterPruneOlderThan()
    {
        var uniqueTerm = $"UNIQUE_{Guid.NewGuid():N}";
        var entry = MakeEntry(
            timestamp: DateTimeOffset.UtcNow.AddHours(-2),
            content: $"{{\"marker\":\"{uniqueTerm}\"}}");
        await _storage.WriteAsync([entry]);

        await _storage.PruneOlderThanAsync(DateTimeOffset.UtcNow.AddHours(-1));

        var results = await _storage.SearchAsync(uniqueTerm, 10);
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task FtsIndex_IsConsistent_AfterPruneToMaxCount()
    {
        var uniqueTerm = $"UNIQUE_{Guid.NewGuid():N}";
        // This entry will be the oldest → pruned when cap = 0.
        var oldest = MakeEntry(
            timestamp: DateTimeOffset.UtcNow.AddMinutes(-10),
            content: $"{{\"marker\":\"{uniqueTerm}\"}}");
        var newest = MakeEntry(
            timestamp: DateTimeOffset.UtcNow,
            content: "{\"marker\":\"keeper\"}");
        await _storage.WriteAsync([oldest, newest]);

        await _storage.PruneToMaxCountAsync(1);

        var results = await _storage.SearchAsync(uniqueTerm, 10);
        results.Should().BeEmpty();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task SeedEntriesAsync(int count, DateTimeOffset baseTimestamp)
    {
        var entries = Enumerable.Range(0, count)
            .Select(i => MakeEntry(baseTimestamp.AddMilliseconds(i)))
            .ToList();
        await _storage.WriteAsync(entries);
    }

    private static LookoutEntry MakeEntry(
        DateTimeOffset? timestamp = null,
        string content = "{}") =>
        new(
            Id: Guid.NewGuid(),
            Type: "test",
            Timestamp: timestamp ?? DateTimeOffset.UtcNow,
            RequestId: null,
            DurationMs: null,
            Tags: new Dictionary<string, string>(),
            Content: content);
}
