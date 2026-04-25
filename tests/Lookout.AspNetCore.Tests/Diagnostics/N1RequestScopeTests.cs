using System.Text.RegularExpressions;
using FluentAssertions;
using Lookout.Core;
using Lookout.Core.Diagnostics;
using Xunit;

namespace Lookout.AspNetCore.Tests.Diagnostics;

public sealed class N1RequestScopeTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private sealed class CapturingRecorder : ILookoutRecorder
    {
        private readonly List<LookoutEntry> _entries = [];
        public IReadOnlyList<LookoutEntry> Entries => _entries;
        public void Record(LookoutEntry entry) => _entries.Add(entry);
    }

    private static LookoutEntry MakeDbEntry(string type = "ef") =>
        new(
            Id: Guid.NewGuid(),
            Type: type,
            Timestamp: DateTimeOffset.UtcNow,
            RequestId: "req-1",
            DurationMs: 1.0,
            Tags: new Dictionary<string, string> { ["db.system"] = type },
            Content: "{}");

    // ── Begin / Current / Dispose ─────────────────────────────────────────────

    [Fact]
    public void Begin_SetsCurrent()
    {
        using var scope = N1RequestScope.Begin(new EfOptions());

        N1RequestScope.Current.Should().BeSameAs(scope);
    }

    [Fact]
    public void Dispose_ClearsCurrent()
    {
        var scope = N1RequestScope.Begin(new EfOptions());

        scope.Dispose();

        N1RequestScope.Current.Should().BeNull();
    }

    [Fact]
    public void Current_IsNull_WhenNoScopeActive()
    {
        // Make sure any scope from another test is gone.
        N1RequestScope.Current.Should().BeNull();
    }

    // ── DbCount ───────────────────────────────────────────────────────────────

    [Fact]
    public void DbCount_IncreasesWithEachTrackedEntry()
    {
        var recorder = new CapturingRecorder();
        using var scope = N1RequestScope.Begin(new EfOptions());

        scope.Track(MakeDbEntry(), "SELECT 1");
        scope.Track(MakeDbEntry(), "SELECT 2");
        scope.DbCount.Should().Be(2);

        scope.Complete(recorder);
    }

    [Fact]
    public void DbCount_ReflectsCountAfterComplete()
    {
        var recorder = new CapturingRecorder();
        using var scope = N1RequestScope.Begin(new EfOptions());

        scope.Track(MakeDbEntry(), "SELECT 1");
        scope.Track(MakeDbEntry(), "SELECT 1");
        scope.Track(MakeDbEntry(), "SELECT 1");

        scope.Complete(recorder);

        scope.DbCount.Should().Be(3);
    }

    // ── Complete: entries below threshold ─────────────────────────────────────

    [Fact]
    public void Complete_FlushesAllEntries_ToRecorder()
    {
        var recorder = new CapturingRecorder();
        using var scope = N1RequestScope.Begin(new EfOptions());
        scope.Track(MakeDbEntry(), "SELECT 1");
        scope.Track(MakeDbEntry(), "SELECT 2");

        scope.Complete(recorder);

        recorder.Entries.Should().HaveCount(2);
    }

    [Fact]
    public void Complete_NoN1Tags_WhenBelowThreshold()
    {
        var recorder = new CapturingRecorder();
        using var scope = N1RequestScope.Begin(new EfOptions()); // threshold = 3
        var sql = "SELECT * FROM Orders WHERE Id = @p0";
        scope.Track(MakeDbEntry(), sql);
        scope.Track(MakeDbEntry(), sql);
        // only 2 — below default threshold of 3

        scope.Complete(recorder);

        recorder.Entries.Should().HaveCount(2);
        recorder.Entries.Should().NotContain(e => e.Tags.ContainsKey("n1.group"),
            "entries below threshold must not receive N+1 tags");
    }

    [Fact]
    public void Complete_ReturnsEmptyGroups_WhenBelowThreshold()
    {
        var recorder = new CapturingRecorder();
        using var scope = N1RequestScope.Begin(new EfOptions());
        scope.Track(MakeDbEntry(), "SELECT 1");
        scope.Track(MakeDbEntry(), "SELECT 1");

        var groups = scope.Complete(recorder);

        groups.Should().BeEmpty();
    }

    // ── Complete: N+1 detected ────────────────────────────────────────────────

    [Fact]
    public void Complete_TagsAllEntriesInGroup_WhenThresholdMet()
    {
        var recorder = new CapturingRecorder();
        using var scope = N1RequestScope.Begin(new EfOptions()); // threshold = 3
        var sql = "SELECT * FROM Orders WHERE Id = @p0";
        scope.Track(MakeDbEntry(), sql);
        scope.Track(MakeDbEntry(), sql);
        scope.Track(MakeDbEntry(), sql);

        scope.Complete(recorder);

        recorder.Entries.Should().HaveCount(3);
        recorder.Entries.Should().OnlyContain(e => e.Tags.ContainsKey("n1.group"),
            "every entry in a detected N+1 group must receive the n1.group tag");
        recorder.Entries.Should().OnlyContain(e => e.Tags["n1.count"] == "3",
            "n1.count must match the group size");
    }

    [Fact]
    public void Complete_ReturnsOneGroup_WhenThresholdMet()
    {
        var recorder = new CapturingRecorder();
        using var scope = N1RequestScope.Begin(new EfOptions());
        var sql = "SELECT * FROM Orders WHERE Id = @p0";
        for (var i = 0; i < 3; i++) scope.Track(MakeDbEntry(), sql);

        var groups = scope.Complete(recorder);

        groups.Should().ContainSingle();
        groups[0].Count.Should().Be(3);
    }

    [Fact]
    public void Complete_N1GroupTag_IsSameForAllEntriesInGroup()
    {
        var recorder = new CapturingRecorder();
        using var scope = N1RequestScope.Begin(new EfOptions());
        var sql = "SELECT * FROM Orders WHERE Id = @p0";
        for (var i = 0; i < 4; i++) scope.Track(MakeDbEntry(), sql);

        scope.Complete(recorder);

        var groupValues = recorder.Entries.Select(e => e.Tags["n1.group"]).Distinct();
        groupValues.Should().ContainSingle("all entries in a group share the same n1.group hash");
    }

    [Fact]
    public void Complete_N1CountTag_ReflectsActualGroupSize()
    {
        var recorder = new CapturingRecorder();
        using var scope = N1RequestScope.Begin(new EfOptions());
        var sql = "SELECT * FROM Customers WHERE Id = @p0";
        for (var i = 0; i < 5; i++) scope.Track(MakeDbEntry(), sql);

        scope.Complete(recorder);

        recorder.Entries.Should().OnlyContain(e => e.Tags["n1.count"] == "5");
    }

    // ── Complete: two groups ──────────────────────────────────────────────────

    [Fact]
    public void Complete_ReturnsTwoGroups_WhenTwoShapesExceedThreshold()
    {
        var recorder = new CapturingRecorder();
        using var scope = N1RequestScope.Begin(new EfOptions());
        for (var i = 0; i < 3; i++) scope.Track(MakeDbEntry(), "SELECT * FROM Orders WHERE Id = @p0");
        for (var i = 0; i < 3; i++) scope.Track(MakeDbEntry(), "SELECT * FROM Customers WHERE Id = @p0");

        var groups = scope.Complete(recorder);

        groups.Should().HaveCount(2);
        recorder.Entries.Should().HaveCount(6);
        recorder.Entries.Should().OnlyContain(e => e.Tags.ContainsKey("n1.group"));
    }

    [Fact]
    public void Complete_OnlyTagsGroupsAboveThreshold_WhenMixed()
    {
        var recorder = new CapturingRecorder();
        using var scope = N1RequestScope.Begin(new EfOptions()); // threshold = 3

        // ShapeA: 3 occurrences — above threshold
        for (var i = 0; i < 3; i++) scope.Track(MakeDbEntry(), "SELECT * FROM Orders WHERE Id = @p0");
        // ShapeB: 2 occurrences — below threshold
        scope.Track(MakeDbEntry(), "SELECT * FROM Customers WHERE Id = @p0");
        scope.Track(MakeDbEntry(), "SELECT * FROM Customers WHERE Id = @p0");

        var groups = scope.Complete(recorder);

        groups.Should().ContainSingle("only ShapeA meets the threshold");
        recorder.Entries.Where(e => e.Tags.ContainsKey("n1.group")).Should().HaveCount(3,
            "only the 3 ShapeA entries get N+1 tags");
        recorder.Entries.Where(e => !e.Tags.ContainsKey("n1.group")).Should().HaveCount(2,
            "ShapeB entries below threshold must not be tagged");
    }

    // ── N1IgnorePatterns ──────────────────────────────────────────────────────

    [Fact]
    public void Complete_IgnoresPattern_WhenShapeKeyMatches()
    {
        var options = new EfOptions
        {
            N1IgnorePatterns = [new Regex("ORDERS", RegexOptions.IgnoreCase)],
        };
        var recorder = new CapturingRecorder();
        using var scope = N1RequestScope.Begin(options);
        for (var i = 0; i < 5; i++)
            scope.Track(MakeDbEntry(), "SELECT * FROM Orders WHERE Id = @p0"); // matches ORDERS

        var groups = scope.Complete(recorder);

        groups.Should().BeEmpty("ORDERS pattern is ignored");
        recorder.Entries.Should().NotContain(e => e.Tags.ContainsKey("n1.group"));
    }

    // ── Mixed EF + ADO entries ────────────────────────────────────────────────

    [Fact]
    public void Complete_MixedEfAndSqlTypes_BothCountedInN1Detection()
    {
        var recorder = new CapturingRecorder();
        using var scope = N1RequestScope.Begin(new EfOptions()); // threshold = 3
        var sql = "SELECT * FROM Orders WHERE Id = @p0";

        scope.Track(MakeDbEntry("ef"), sql);
        scope.Track(MakeDbEntry("ef"), sql);
        scope.Track(MakeDbEntry("sql"), sql); // same shape from ADO

        var groups = scope.Complete(recorder);

        groups.Should().ContainSingle("3 queries of the same shape from mixed sources constitute N+1");
        recorder.Entries.Should().HaveCount(3);
        recorder.Entries.Should().OnlyContain(e => e.Tags.ContainsKey("n1.group"));
    }

    // ── Out-of-request (no scope) ─────────────────────────────────────────────

    [Fact]
    public void Current_IsNull_WhenNoScopeCreated()
    {
        // Ensure no scope is active (clean state between tests via Dispose).
        N1RequestScope.Current.Should().BeNull();
    }

    // ── ComputeShapeHash ──────────────────────────────────────────────────────

    [Fact]
    public void ComputeShapeHash_IsDeterministic()
    {
        var h1 = N1RequestScope.ComputeShapeHash("SELECT * FROM ORDERS WHERE ID = ?");
        var h2 = N1RequestScope.ComputeShapeHash("SELECT * FROM ORDERS WHERE ID = ?");

        h1.Should().Be(h2);
    }

    [Fact]
    public void ComputeShapeHash_DifferentShapes_ProduceDifferentHashes()
    {
        var h1 = N1RequestScope.ComputeShapeHash("SELECT * FROM ORDERS WHERE ID = ?");
        var h2 = N1RequestScope.ComputeShapeHash("SELECT * FROM CUSTOMERS WHERE ID = ?");

        h1.Should().NotBe(h2);
    }

    [Fact]
    public void ComputeShapeHash_Returns8CharLowercaseHex()
    {
        var hash = N1RequestScope.ComputeShapeHash("SELECT 1");

        hash.Should().HaveLength(8);
        hash.Should().MatchRegex("^[0-9a-f]{8}$");
    }
}
