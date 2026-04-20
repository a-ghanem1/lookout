using FluentAssertions;
using Lookout.Core;
using Lookout.Storage.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lookout.AspNetCore.Tests.Pipeline;

public sealed class LookoutPipelineTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in _tempFiles)
            foreach (var f in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(f)) File.Delete(f);
    }

    // ── Tag callback ──────────────────────────────────────────────────────────

    [Fact]
    public void Tag_Callback_AddsTags_BeforeEnqueue()
    {
        var recorder = BuildRecorder(o => o.Tag = (_, tags) => tags["env"] = "test");

        recorder.Record(MakeEntry());

        recorder.Reader.TryRead(out var enqueued).Should().BeTrue();
        enqueued!.Tags["env"].Should().Be("test");
    }

    [Fact]
    public void Tag_Callback_ReceivesOriginalEntry()
    {
        LookoutEntry? received = null;
        var original = MakeEntry(type: "http");
        var recorder = BuildRecorder(o => o.Tag = (e, _) => received = e);

        recorder.Record(original);

        received.Should().Be(original);
    }

    // ── Filter callback ───────────────────────────────────────────────────────

    [Fact]
    public void Filter_Callback_DropsEntry_WhenReturnsFalse()
    {
        var recorder = BuildRecorder(o => o.Filter = _ => false);

        recorder.Record(MakeEntry());

        recorder.Reader.TryRead(out _).Should().BeFalse("filtered entry must not reach the channel");
    }

    [Fact]
    public void Filter_Callback_PassesEntry_WhenReturnsTrue()
    {
        var recorder = BuildRecorder(o => o.Filter = _ => true);

        recorder.Record(MakeEntry());

        recorder.Reader.TryRead(out _).Should().BeTrue();
    }

    [Fact]
    public void Filter_Callback_SeesTagsAppliedByTagCallback()
    {
        string? seenTag = null;
        var recorder = BuildRecorder(o =>
        {
            o.Tag = (_, tags) => tags["stage"] = "tagged";
            o.Filter = e => { seenTag = e.Tags.GetValueOrDefault("stage"); return true; };
        });

        recorder.Record(MakeEntry());

        seenTag.Should().Be("tagged", "Filter runs after Tag on the same entry");
    }

    // ── RedactionPipeline unit tests ──────────────────────────────────────────

    [Fact]
    public void Redaction_MasksTagKeys_InHeadersSet()
    {
        var options = new RedactionOptions();
        options.Headers.Add("Authorization");

        var entry = MakeEntry(tags: new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer secret",
            ["safe"] = "visible",
        });

        var result = RedactionPipeline.Apply(entry, options);

        result.Tags["Authorization"].Should().Be("***");
        result.Tags["safe"].Should().Be("visible");
    }

    [Fact]
    public void Redaction_MasksContentJsonKeys_InQueryParamsSet()
    {
        var options = new RedactionOptions();
        options.QueryParams.Add("password");

        var entry = MakeEntry(content: """{"password":"s3cr3t","username":"alice"}""");

        var result = RedactionPipeline.Apply(entry, options);

        result.Content.Should().Contain("\"***\"");
        result.Content.Should().Contain("\"username\"");
        result.Content.Should().NotContain("s3cr3t");
    }

    [Fact]
    public void Redaction_ReturnsSameInstance_WhenNothingToRedact()
    {
        var options = new RedactionOptions { Headers = new(StringComparer.OrdinalIgnoreCase) };
        options.QueryParams.Clear();
        options.FormFields.Clear();

        var entry = MakeEntry(content: """{"username":"alice"}""");

        var result = RedactionPipeline.Apply(entry, options);

        result.Should().BeSameAs(entry);
    }

    [Fact]
    public void Redaction_IsCaseInsensitive_ForTagKeys()
    {
        var options = new RedactionOptions();
        options.Headers.Add("authorization");

        var entry = MakeEntry(tags: new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer secret",
        });

        var result = RedactionPipeline.Apply(entry, options);

        result.Tags["Authorization"].Should().Be("***");
    }

    // ── FilterBatch + Redact in the flusher (integration) ────────────────────

    [Fact]
    public async Task FilterBatch_Callback_IsApplied_InFlusher()
    {
        var dbPath = TempDbPath();
        await using var app = BuildTestApp(dbPath, o =>
        {
            o.FilterBatch = batch => batch.Where(e => e.Type != "drop").ToList();
        });
        await app.StartAsync();

        var recorder = app.Services.GetRequiredService<ILookoutRecorder>();
        recorder.Record(MakeEntry(type: "keep"));
        recorder.Record(MakeEntry(type: "drop"));
        recorder.Record(MakeEntry(type: "keep"));

        var rows = await PollForRowCountAsync(dbPath, expected: 2, timeoutSeconds: 5);
        await app.StopAsync();

        rows.Should().Be(2, "the 'drop' entry must be filtered before write");
    }

    [Fact]
    public async Task DefaultRedaction_MasksHeaders_BeforeStorage()
    {
        var dbPath = TempDbPath();
        await using var app = BuildTestApp(dbPath, o =>
        {
            o.Redaction.Headers.Add("X-Secret");
        });
        await app.StartAsync();

        var recorder = app.Services.GetRequiredService<ILookoutRecorder>();
        recorder.Record(MakeEntry(tags: new Dictionary<string, string> { ["X-Secret"] = "topsecret" }));

        await PollForRowCountAsync(dbPath, expected: 1, timeoutSeconds: 5);
        await app.StopAsync();

        using var storage = OpenReadStorage(dbPath);
        var entries = await storage.ReadRecentAsync(5);
        entries[0].Tags["X-Secret"].Should().Be("***");
    }

    [Fact]
    public async Task CustomRedact_Callback_RunsAfterDefaultRedaction()
    {
        var dbPath = TempDbPath();
        string? seenTagValue = null;

        await using var app = BuildTestApp(dbPath, o =>
        {
            o.Redaction.Headers.Add("Authorization");
            o.Redact = e =>
            {
                seenTagValue = e.Tags.GetValueOrDefault("Authorization");
                return e;
            };
        });
        await app.StartAsync();

        var recorder = app.Services.GetRequiredService<ILookoutRecorder>();
        recorder.Record(MakeEntry(tags: new Dictionary<string, string> { ["Authorization"] = "Bearer raw" }));

        await PollForRowCountAsync(dbPath, expected: 1, timeoutSeconds: 5);
        await app.StopAsync();

        seenTagValue.Should().Be("***", "custom Redact must see the already-masked value");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static ChannelLookoutRecorder BuildRecorder(Action<LookoutOptions>? configure = null)
    {
        var opts = new LookoutOptions();
        configure?.Invoke(opts);
        return new ChannelLookoutRecorder(
            Options.Create(opts),
            NullLogger<ChannelLookoutRecorder>.Instance);
    }

    private string TempDbPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lookout_pipeline_{Guid.NewGuid():N}.db");
        _tempFiles.Add(path);
        return path;
    }

    private static WebApplication BuildTestApp(string dbPath, Action<LookoutOptions>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddLookout(o =>
        {
            o.StoragePath = dbPath;
            configure?.Invoke(o);
        });
        var app = builder.Build();
        app.UseLookout();
        app.MapLookout();
        return app;
    }

    private static SqliteLookoutStorage OpenReadStorage(string dbPath) =>
        new(new LookoutOptions { StoragePath = dbPath });

    private static async Task<int> PollForRowCountAsync(
        string dbPath, int expected, int timeoutSeconds)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        int count = 0;
        while (count < expected && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50);
            using var storage = OpenReadStorage(dbPath);
            count = (await storage.ReadRecentAsync(expected + 10)).Count;
        }
        return count;
    }

    private static LookoutEntry MakeEntry(
        string type = "test",
        string content = "{}",
        IReadOnlyDictionary<string, string>? tags = null) =>
        new(
            Id: Guid.NewGuid(),
            Type: type,
            Timestamp: DateTimeOffset.UtcNow,
            RequestId: null,
            DurationMs: null,
            Tags: tags ?? new Dictionary<string, string>(),
            Content: content);
}
