using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Lookout.AspNetCore.Capture;
using Lookout.Core;
using Lookout.Core.Capture;
using Lookout.Core.Schemas;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lookout.AspNetCore.Tests.AdoNet;

public sealed class AdoNetDiagnosticSubscriberTests : IDisposable
{
    // ── Test helpers ─────────────────────────────────────────────────────────

    private sealed class CapturingRecorder : ILookoutRecorder
    {
        private readonly List<LookoutEntry> _entries = [];

        public IReadOnlyList<LookoutEntry> Entries
        {
            get { lock (_entries) return _entries.ToList(); }
        }

        public void Record(LookoutEntry entry) { lock (_entries) _entries.Add(entry); }

        public void Clear() { lock (_entries) _entries.Clear(); }
    }

    // ── Fixtures ──────────────────────────────────────────────────────────────
    private readonly SqliteConnection _connection;

    public AdoNetDiagnosticSubscriberTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private static (AdoNetDiagnosticSubscriber Subscriber, CapturingRecorder Recorder)
        Build(Action<LookoutOptions>? configure = null)
    {
        var opts = new LookoutOptions();
        configure?.Invoke(opts);
        var recorder = new CapturingRecorder();
        var sub = new AdoNetDiagnosticSubscriber(recorder, Options.Create(opts));
        return (sub, recorder);
    }

    // ── Entry type and tags ───────────────────────────────────────────────────

    [Fact]
    public void RecordAdoCommand_RecordsEntry_WithSqlType()
    {
        var (sub, recorder) = Build();
        using var cmd = new SqliteCommand("SELECT 1", _connection);

        sub.RecordAdoCommand(cmd, durationMs: 5.0, rowsAffected: null);

        recorder.Entries.Should().ContainSingle(e => e.Type == "sql");
    }

    [Fact]
    public void RecordAdoCommand_Tags_DbSystemIsAdo()
    {
        var (sub, recorder) = Build();
        using var cmd = new SqliteCommand("SELECT 1", _connection);

        sub.RecordAdoCommand(cmd, durationMs: 0, rowsAffected: null);

        recorder.Entries.Single().Tags["db.system"].Should().Be("ado");
    }

    [Fact]
    public void RecordAdoCommand_Tags_DbProviderFromConnectionType()
    {
        var (sub, recorder) = Build();
        using var cmd = new SqliteCommand("SELECT 1", _connection);

        sub.RecordAdoCommand(cmd, durationMs: 0, rowsAffected: null);

        recorder.Entries.Single().Tags.Should().ContainKey("db.provider");
        recorder.Entries.Single().Tags["db.provider"].Should().Contain("Sqlite");
    }

    // ── Content fields ────────────────────────────────────────────────────────

    [Fact]
    public void RecordAdoCommand_Content_CommandTextPreserved()
    {
        var (sub, recorder) = Build();
        using var cmd = new SqliteCommand("SELECT 42", _connection);

        sub.RecordAdoCommand(cmd, durationMs: 0, rowsAffected: null);

        var content = Deserialize(recorder.Entries.Single());
        content.CommandText.Should().Be("SELECT 42");
    }

    [Fact]
    public void RecordAdoCommand_Content_DurationMsSet()
    {
        var (sub, recorder) = Build();
        using var cmd = new SqliteCommand("SELECT 1", _connection);

        sub.RecordAdoCommand(cmd, durationMs: 12.5, rowsAffected: null);

        var entry = recorder.Entries.Single();
        entry.DurationMs.Should().Be(12.5);
        Deserialize(entry).DurationMs.Should().Be(12.5);
    }

    [Fact]
    public void RecordAdoCommand_Content_CommandTypeIsText()
    {
        var (sub, recorder) = Build();
        using var cmd = new SqliteCommand("SELECT 1", _connection);

        sub.RecordAdoCommand(cmd, durationMs: 0, rowsAffected: null);

        Deserialize(recorder.Entries.Single()).CommandType.Should().Be("Text");
    }

    [Fact]
    public void RecordAdoCommand_Content_RowsAffectedNull_WhenNotProvided()
    {
        var (sub, recorder) = Build();
        using var cmd = new SqliteCommand("SELECT 1", _connection);

        sub.RecordAdoCommand(cmd, durationMs: 0, rowsAffected: null);

        Deserialize(recorder.Entries.Single()).RowsAffected.Should().BeNull();
    }

    // ── Stack trace ───────────────────────────────────────────────────────────

    [Fact]
    public void RecordAdoCommand_CapturesStackTrace_WithUserCodeFrames()
    {
        var (sub, recorder) = Build();
        using var cmd = new SqliteCommand("SELECT 1", _connection);

        sub.RecordAdoCommand(cmd, durationMs: 0, rowsAffected: null);

        var content = Deserialize(recorder.Entries.Single());
        content.Stack.Should().NotBeEmpty();
        content.Stack.Should().Contain(
            f => f.Method.Contains(nameof(RecordAdoCommand_CapturesStackTrace_WithUserCodeFrames)),
            "the calling test method must appear in the captured stack");
    }

    [Fact]
    public void RecordAdoCommand_StackTrace_RespectsMaxStackFramesCap()
    {
        var (sub, recorder) = Build(o => o.Ef.MaxStackFrames = 1);
        using var cmd = new SqliteCommand("SELECT 1", _connection);

        sub.RecordAdoCommand(cmd, durationMs: 0, rowsAffected: null);

        Deserialize(recorder.Entries.Single()).Stack.Should().HaveCountLessThanOrEqualTo(1);
    }

    // ── Request correlation ───────────────────────────────────────────────────

    [Fact]
    public void RecordAdoCommand_RequestId_IsNullOutsideActivity()
    {
        var (sub, recorder) = Build();
        using var cmd = new SqliteCommand("SELECT 1", _connection);

        var saved = Activity.Current;
        Activity.Current = null;
        try { sub.RecordAdoCommand(cmd, durationMs: 0, rowsAffected: null); }
        finally { Activity.Current = saved; }

        recorder.Entries.Single().RequestId.Should().BeNull();
    }

    [Fact]
    public void RecordAdoCommand_RequestId_UsesActivityRootIdInsideRequest()
    {
        var (sub, recorder) = Build();
        using var cmd = new SqliteCommand("SELECT 1", _connection);
        using var activity = new Activity("TestRequest").Start();

        sub.RecordAdoCommand(cmd, durationMs: 0, rowsAffected: null);

        recorder.Entries.Single().RequestId.Should().Be(activity.RootId);
    }

    // ── De-dupe: EF wins ──────────────────────────────────────────────────────

    [Fact]
    public void RecordAdoCommand_SkipsEntry_WhenCommandIsMarkedByEfInterceptor()
    {
        var (sub, recorder) = Build();
        using var cmd = new SqliteCommand("SELECT 1", _connection);

        EfCommandRegistry.Mark(cmd);
        sub.RecordAdoCommand(cmd, durationMs: 0, rowsAffected: null);

        recorder.Entries.Should().BeEmpty("EF-owned commands must not produce a sql entry");
    }

    [Fact]
    public void RecordAdoCommand_RecordsEntry_WhenCommandIsNotMarked()
    {
        var (sub, recorder) = Build();
        using var cmd = new SqliteCommand("SELECT 1", _connection);
        // No EfCommandRegistry.Mark — should record
        sub.RecordAdoCommand(cmd, durationMs: 0, rowsAffected: null);

        recorder.Entries.Should().ContainSingle();
    }

    // ── Parameter capture ─────────────────────────────────────────────────────

    [Fact]
    public void RecordAdoCommand_CapturesParameterValues_WhenEnabled()
    {
        var (sub, recorder) = Build(); // CaptureParameterValues defaults to true
        using var cmd = new SqliteCommand("SELECT * FROM T WHERE Id = @id", _connection);
        cmd.Parameters.AddWithValue("@id", 42);

        sub.RecordAdoCommand(cmd, durationMs: 0, rowsAffected: null);

        var content = Deserialize(recorder.Entries.Single());
        content.Parameters.Should().ContainSingle(p => p.Name == "@id" && p.Value == "42");
    }

    [Fact]
    public void RecordAdoCommand_OmitsParameterValues_WhenCaptureParameterValuesIsFalse()
    {
        var (sub, recorder) = Build(o => o.Ef.CaptureParameterValues = false);
        using var cmd = new SqliteCommand("SELECT * FROM T WHERE Id = @id", _connection);
        cmd.Parameters.AddWithValue("@id", 42);

        sub.RecordAdoCommand(cmd, durationMs: 0, rowsAffected: null);

        var content = Deserialize(recorder.Entries.Single());
        content.Parameters.Should().ContainSingle(p => p.Name == "@id" && p.Value == null);
    }

    [Fact]
    public void RecordAdoCommand_RedactsParameter_WhenNameMatchesSensitiveList()
    {
        var (sub, recorder) = Build(); // "password" is in the default redaction list
        using var cmd = new SqliteCommand("SELECT 1 WHERE @password IS NOT NULL", _connection);
        cmd.Parameters.AddWithValue("@password", "secret");

        sub.RecordAdoCommand(cmd, durationMs: 0, rowsAffected: null);

        var content = Deserialize(recorder.Entries.Single());
        content.Parameters.Should().ContainSingle(p =>
            p.Name == "@password" && p.Value == "***",
            "password parameters must be redacted");
    }

    [Fact]
    public void RecordAdoCommand_CapturesOnlyTypes_WhenCaptureParameterTypesOnly()
    {
        var (sub, recorder) = Build(o => o.Ef.CaptureParameterTypesOnly = true);
        using var cmd = new SqliteCommand("SELECT * FROM T WHERE Id = @id", _connection);
        cmd.Parameters.AddWithValue("@id", 42);

        sub.RecordAdoCommand(cmd, durationMs: 0, rowsAffected: null);

        var content = Deserialize(recorder.Entries.Single());
        content.Parameters.Should().ContainSingle(p =>
            p.Name == "@id" && p.Value == null && p.DbType != null);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_AndStopAsync_CompleteWithoutError()
    {
        var (sub, _) = Build();

        await sub.StartAsync(CancellationToken.None);
        await sub.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void WatchedSources_ContainsSqlClientDiagnosticListener()
    {
        AdoNetDiagnosticSubscriber.WatchedSources
            .Should().Contain("SqlClientDiagnosticListener");
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static SqlEntryContent Deserialize(LookoutEntry entry) =>
        JsonSerializer.Deserialize<SqlEntryContent>(entry.Content, LookoutJson.Options)!;
}
