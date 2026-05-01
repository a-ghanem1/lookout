using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FluentAssertions;
using Lookout.Core;
using Lookout.Core.Schemas;
using Lookout.EntityFrameworkCore.IntegrationTests.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lookout.EntityFrameworkCore.IntegrationTests;

public sealed class LookoutDbCommandInterceptorTests
{
    // ── factory helpers ────────────────────────────────────────────────────────

    private static (LookoutDbCommandInterceptor Interceptor, CapturingRecorder Recorder)
        BuildInterceptor(Action<LookoutOptions>? configure = null)
    {
        var opts = new LookoutOptions();
        configure?.Invoke(opts);
        var recorder = new CapturingRecorder();
        var interceptor = new LookoutDbCommandInterceptor(recorder, Options.Create(opts));
        return (interceptor, recorder);
    }

    // Named shared-cache in-memory database: EF Core may open/close connections freely,
    // but all connections to the same name share one database. The returned `Conn` is kept
    // open so the in-memory database isn't destroyed between operations.
    private static async Task<(TestDbContext Ctx, SqliteConnection Conn)> BuildContextAsync(
        LookoutDbCommandInterceptor interceptor)
    {
        var dbName = $"lookout_{Guid.NewGuid():N}";
        var cs = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        var conn = new SqliteConnection(cs);
        await conn.OpenAsync();

        var builder = new DbContextOptionsBuilder<TestDbContext>();
        builder.UseSqlite(cs);
        builder.UseLookout(interceptor);

        var ctx = new TestDbContext(builder.Options);
        await ctx.Database.EnsureCreatedAsync();
        return (ctx, conn);
    }

    // ── tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Query_RecordsEfEntry_WithExpectedContentAndTags()
    {
        var (interceptor, recorder) = BuildInterceptor();
        var (ctx, conn) = await BuildContextAsync(interceptor);
        await using (ctx) await using (conn)
        {
            recorder.Clear(); // discard EnsureCreated entries

            var _ = await ctx.Widgets.ToListAsync();

            var efEntries = recorder.Entries.Where(e => e.Type == "ef").ToList();
            efEntries.Should().NotBeEmpty();

            var entry = efEntries[0];
            entry.Tags["db.system"].Should().Be("ef");
            entry.Tags.Should().ContainKey("db.provider");
            entry.Tags.Should().ContainKey("db.context");
            entry.DurationMs.Should().BeGreaterThanOrEqualTo(0);

            var content = JsonSerializer.Deserialize<EfEntryContent>(entry.Content, LookoutJson.Options);
            content.Should().NotBeNull();
            content!.CommandText.Should().NotBeNullOrEmpty();
            content.DurationMs.Should().BeGreaterThanOrEqualTo(0);
            content.CommandType.Should().Be(EfCommandType.Reader);
            content.Stack.Should().NotBeEmpty("M4.3 enables stack capture by default");
        }
    }

    [Fact]
    public async Task NonQuery_RecordsRowsAffectedAndTag()
    {
        var (interceptor, recorder) = BuildInterceptor();
        var (ctx, conn) = await BuildContextAsync(interceptor);
        await using (ctx) await using (conn)
        {
            recorder.Clear();

            // ExecuteSqlRawAsync always fires NonQueryExecuted (unlike SaveChanges which may
            // use ReaderExecuted to fetch the auto-generated key via SELECT last_insert_rowid()).
            await ctx.Database.ExecuteSqlRawAsync("INSERT INTO Widgets (Name) VALUES ('Alpha')");

            var nonQueryEntries = recorder.Entries
                .Where(e => e.Type == "ef" && e.Tags.ContainsKey("db.rows"))
                .ToList();
            nonQueryEntries.Should().NotBeEmpty("ExecuteSqlRawAsync fires NonQueryExecuted with rows affected");

            var entry = nonQueryEntries[0];
            entry.Tags["db.rows"].Should().Be("1");

            var content = JsonSerializer.Deserialize<EfEntryContent>(entry.Content, LookoutJson.Options)!;
            content.RowsAffected.Should().Be(1);
            content.CommandType.Should().Be(EfCommandType.NonQuery);
        }
    }

    [Fact]
    public async Task CaptureParameterValues_Default_RecordsParameterValues()
    {
        var (interceptor, recorder) = BuildInterceptor();
        var (ctx, conn) = await BuildContextAsync(interceptor);
        await using (ctx) await using (conn)
        {
            recorder.Clear();

            ctx.Widgets.Add(new Widget { Name = "Beta" });
            await ctx.SaveChangesAsync();

            var paramEntries = recorder.Entries
                .Where(e => e.Type == "ef")
                .Select(e => JsonSerializer.Deserialize<EfEntryContent>(e.Content, LookoutJson.Options)!)
                .Where(c => c.Parameters.Count > 0)
                .ToList();

            paramEntries.Should().NotBeEmpty();
            paramEntries[0].Parameters.Should().OnlyContain(p => p.Value != null,
                "default CaptureParameterValues=true records values");
        }
    }

    [Fact]
    public async Task CaptureParameterTypesOnly_RecordsTypesWithoutValues()
    {
        var (interceptor, recorder) = BuildInterceptor(o => o.Ef.CaptureParameterTypesOnly = true);
        var (ctx, conn) = await BuildContextAsync(interceptor);
        await using (ctx) await using (conn)
        {
            recorder.Clear();

            ctx.Widgets.Add(new Widget { Name = "Gamma" });
            await ctx.SaveChangesAsync();

            var paramEntries = recorder.Entries
                .Where(e => e.Type == "ef")
                .Select(e => JsonSerializer.Deserialize<EfEntryContent>(e.Content, LookoutJson.Options)!)
                .Where(c => c.Parameters.Count > 0)
                .ToList();

            paramEntries.Should().NotBeEmpty();
            foreach (var content in paramEntries)
            {
                content.Parameters.Should().OnlyContain(p => p.Value == null,
                    "CaptureParameterTypesOnly=true must never store values");
                content.Parameters.Should().OnlyContain(p => p.DbType != null,
                    "DbType should always be captured");
            }
        }
    }

    [Fact]
    public async Task CaptureParameterValues_False_RecordsNullValues()
    {
        var (interceptor, recorder) = BuildInterceptor(o =>
        {
            o.Ef.CaptureParameterValues = false;
            o.Ef.CaptureParameterTypesOnly = false;
        });
        var (ctx, conn) = await BuildContextAsync(interceptor);
        await using (ctx) await using (conn)
        {
            recorder.Clear();

            ctx.Widgets.Add(new Widget { Name = "Delta" });
            await ctx.SaveChangesAsync();

            var paramEntries = recorder.Entries
                .Where(e => e.Type == "ef")
                .Select(e => JsonSerializer.Deserialize<EfEntryContent>(e.Content, LookoutJson.Options)!)
                .Where(c => c.Parameters.Count > 0)
                .ToList();

            paramEntries.Should().NotBeEmpty();
            paramEntries[0].Parameters.Should().OnlyContain(p => p.Value == null,
                "CaptureParameterValues=false must not record values");
        }
    }

    [Fact]
    public async Task Redaction_MasksPasswordParameter()
    {
        var (interceptor, recorder) = BuildInterceptor(o => o.Ef.CaptureParameterValues = true);
        var dbName = $"lookout_{Guid.NewGuid():N}";
        var cs = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        await using var conn = new SqliteConnection(cs);
        await conn.OpenAsync();

        var ctxBuilder = new DbContextOptionsBuilder<TestDbContext>();
        ctxBuilder.UseSqlite(cs);
        ctxBuilder.UseLookout(interceptor);
        await using var ctx = new TestDbContext(ctxBuilder.Options);
        await ctx.Database.EnsureCreatedAsync();
        recorder.Clear();

        // Issue a raw parameterised command with a parameter literally named @password.
        await ctx.Database.ExecuteSqlRawAsync(
            "SELECT 1 WHERE @password IS NOT NULL",
            new SqliteParameter("@password", "secret123"));

        var efEntries = recorder.Entries.Where(e => e.Type == "ef").ToList();
        efEntries.Should().NotBeEmpty();

        var content = JsonSerializer.Deserialize<EfEntryContent>(
            efEntries[^1].Content, LookoutJson.Options)!;
        var passwordParam = content.Parameters
            .FirstOrDefault(p => p.Name.Contains("password", StringComparison.OrdinalIgnoreCase));
        passwordParam.Should().NotBeNull("@password parameter must be captured");
        passwordParam!.Value.Should().Be("***", "password parameters must be redacted");
    }

    [Fact]
    public async Task QueryOutsideRequest_HasNullRequestId()
    {
        var (interceptor, recorder) = BuildInterceptor();
        var (ctx, conn) = await BuildContextAsync(interceptor);
        await using (ctx) await using (conn)
        {
            recorder.Clear();

            // Temporarily clear any ambient Activity so we can assert null RequestId.
            var saved = Activity.Current;
            Activity.Current = null;
            try
            {
                var _ = await ctx.Widgets.ToListAsync();
            }
            finally
            {
                Activity.Current = saved;
            }

            recorder.Entries.Where(e => e.Type == "ef")
                .Should().NotBeEmpty()
                .And.OnlyContain(e => e.RequestId == null,
                    "queries outside a request context must have null RequestId");
        }
    }

    [Fact]
    public async Task QueryInsideActivity_UsesRootIdAsRequestId()
    {
        var (interceptor, recorder) = BuildInterceptor();
        var (ctx, conn) = await BuildContextAsync(interceptor);
        await using (ctx) await using (conn)
        {
            recorder.Clear();

            using var activity = new Activity("TestRequest").Start();

            var _ = await ctx.Widgets.ToListAsync();

            var efEntries = recorder.Entries.Where(e => e.Type == "ef").ToList();
            efEntries.Should().NotBeEmpty();
            efEntries.Should().OnlyContain(e => e.RequestId == activity.RootId,
                "EF entries inside an Activity must use Activity.RootId as RequestId");
        }
    }

    [Fact]
    public async Task DbContextType_IsRecordedInTagAndContent()
    {
        var (interceptor, recorder) = BuildInterceptor();
        var (ctx, conn) = await BuildContextAsync(interceptor);
        await using (ctx) await using (conn)
        {
            recorder.Clear();

            var _ = await ctx.Widgets.ToListAsync();

            var entry = recorder.Entries.First(e => e.Type == "ef");
            entry.Tags["db.context"].Should().Contain(nameof(TestDbContext));

            var content = JsonSerializer.Deserialize<EfEntryContent>(entry.Content, LookoutJson.Options)!;
            content.DbContextType.Should().Contain(nameof(TestDbContext));
        }
    }

    [Fact]
    public async Task StackTrace_ContainsTestMethodFrame_ExcludesFrameworkFrames()
    {
        var (interceptor, recorder) = BuildInterceptor();
        var (ctx, conn) = await BuildContextAsync(interceptor);
        await using (ctx) await using (conn)
        {
            recorder.Clear();

            // Use sync ToList() so the synchronous call stack is preserved all the way
            // up to this method. ToListAsync() breaks the stack at the await continuation.
            _ = ctx.Widgets.ToList();

            var entry = recorder.Entries.First(e => e.Type == "ef");
            var content = JsonSerializer.Deserialize<EfEntryContent>(entry.Content, LookoutJson.Options)!;

            content.Stack.Should().NotBeEmpty("interceptor records user-code frames by default");

            // EF Core and ASP.NET Core framework frames must be absent.
            // Note: System.* frames are also filtered, but async state-machine method names
            // can include compiler-generated types whose namespace varies across runtimes.
            content.Stack.Should().NotContain(
                f => f.Method.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)
                  || f.Method.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase),
                "EF Core and ASP.NET Core framework frames must be filtered out");

            content.Stack.Should().Contain(
                f => f.Method.Contains(nameof(StackTrace_ContainsTestMethodFrame_ExcludesFrameworkFrames)),
                "the calling test method must appear in the user-code stack");
        }
    }

    [Fact]
    public async Task StackTrace_RespectsMaxStackFramesCap()
    {
        var (interceptor, recorder) = BuildInterceptor(o => o.Ef.MaxStackFrames = 1);
        var (ctx, conn) = await BuildContextAsync(interceptor);
        await using (ctx) await using (conn)
        {
            recorder.Clear();

            var _ = await ctx.Widgets.ToListAsync();

            var entry = recorder.Entries.First(e => e.Type == "ef");
            var content = JsonSerializer.Deserialize<EfEntryContent>(entry.Content, LookoutJson.Options)!;

            content.Stack.Should().HaveCountLessThanOrEqualTo(1,
                "MaxStackFrames = 1 must limit captured frames to at most 1");
        }
    }

    [Fact]
    public async Task StackTrace_MethodNameAlwaysPresent_FileAndLineNullable()
    {
        var (interceptor, recorder) = BuildInterceptor();
        var (ctx, conn) = await BuildContextAsync(interceptor);
        await using (ctx) await using (conn)
        {
            recorder.Clear();

            var _ = await ctx.Widgets.ToListAsync();

            var entry = recorder.Entries.First(e => e.Type == "ef");
            var content = JsonSerializer.Deserialize<EfEntryContent>(entry.Content, LookoutJson.Options)!;

            content.Stack.Should().NotBeEmpty();
            content.Stack.Should().OnlyContain(
                f => !string.IsNullOrEmpty(f.Method),
                "Method must always be present regardless of PDB availability; File and Line are nullable");
        }
    }

    [Fact]
    public async Task StackFrame_FilePaths_WhenPresent_AreAbsoluteAndPlatformNative()
    {
        var (interceptor, recorder) = BuildInterceptor();
        var (ctx, conn) = await BuildContextAsync(interceptor);
        await using (ctx) await using (conn)
        {
            recorder.Clear();
            _ = ctx.Widgets.ToList(); // sync: preserves the full synchronous call stack

            var entry = recorder.Entries.First(e => e.Type == "ef");
            var content = JsonSerializer.Deserialize<EfEntryContent>(entry.Content, LookoutJson.Options)!;

            var framesWithFiles = content.Stack.Where(f => f.File is not null).ToList();
            if (framesWithFiles.Count == 0) return; // PDBs not available in this build config

            framesWithFiles.Should().OnlyContain(
                f => Path.IsPathRooted(f.File!),
                "stack frame file paths must be absolute so IDEs can navigate to source");

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                framesWithFiles.Should().NotContain(
                    f => f.File!.Contains('\\'),
                    "on Unix, stack frame file paths must use forward-slash separators");
            }
        }
    }
}
