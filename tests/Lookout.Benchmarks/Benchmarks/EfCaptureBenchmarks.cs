using BenchmarkDotNet.Attributes;
using Lookout.Core;
using Lookout.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lookout.Benchmarks;

/// <summary>
/// Measures the per-query overhead introduced by LookoutDbCommandInterceptor.
/// Three scenarios: no interceptor (baseline), interceptor with stack capture off, and on.
/// Budget: &lt;1 ms added latency per capture (plan anti-goal).
/// </summary>
[MemoryDiagnoser]
public class EfCaptureBenchmarks
{
    private SqliteConnection? _conn;
    private DbContextOptions<EfBenchDbContext>? _optionsBaseline;
    private DbContextOptions<EfBenchDbContext>? _optionsNoStack;
    private DbContextOptions<EfBenchDbContext>? _optionsWithStack;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        var dbName = $"lookout_efbench_{Guid.NewGuid():N}";
        var cs = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        _conn = new SqliteConnection(cs);
        await _conn.OpenAsync().ConfigureAwait(false);

        _optionsBaseline = new DbContextOptionsBuilder<EfBenchDbContext>()
            .UseSqlite(cs)
            .Options;

        using var setupCtx = new EfBenchDbContext(_optionsBaseline);
        await setupCtx.Database.EnsureCreatedAsync().ConfigureAwait(false);
        setupCtx.Widgets.Add(new EfBenchWidget { Name = "Seed" });
        await setupCtx.SaveChangesAsync().ConfigureAwait(false);

        var recorder = new NullRecorder();

        var noStackOpts = new LookoutOptions();
        noStackOpts.Ef.MaxStackFrames = 0;
        var noStackInterceptor = new LookoutDbCommandInterceptor(recorder, Options.Create(noStackOpts));
        _optionsNoStack = new DbContextOptionsBuilder<EfBenchDbContext>()
            .UseSqlite(cs)
            .UseLookout(noStackInterceptor)
            .Options;

        var withStackOpts = new LookoutOptions();
        withStackOpts.Ef.MaxStackFrames = 20;
        var withStackInterceptor = new LookoutDbCommandInterceptor(recorder, Options.Create(withStackOpts));
        _optionsWithStack = new DbContextOptionsBuilder<EfBenchDbContext>()
            .UseSqlite(cs)
            .UseLookout(withStackInterceptor)
            .Options;
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        if (_conn != null) await _conn.DisposeAsync().ConfigureAwait(false);
    }

    [Benchmark(Baseline = true, Description = "No interceptor")]
    public async Task EfQuery_NoInterceptor()
    {
        await using var ctx = new EfBenchDbContext(_optionsBaseline!);
        _ = await ctx.Widgets.ToListAsync().ConfigureAwait(false);
    }

    [Benchmark(Description = "Interceptor, stack off (MaxStackFrames=0)")]
    public async Task EfQuery_Interceptor_NoStack()
    {
        await using var ctx = new EfBenchDbContext(_optionsNoStack!);
        _ = await ctx.Widgets.ToListAsync().ConfigureAwait(false);
    }

    [Benchmark(Description = "Interceptor, stack on (MaxStackFrames=20)")]
    public async Task EfQuery_Interceptor_WithStack()
    {
        await using var ctx = new EfBenchDbContext(_optionsWithStack!);
        _ = await ctx.Widgets.ToListAsync().ConfigureAwait(false);
    }

    private sealed class NullRecorder : ILookoutRecorder
    {
        public void Record(LookoutEntry entry) { }
    }
}

internal sealed class EfBenchWidget { public int Id { get; set; } public string Name { get; set; } = ""; }

internal sealed class EfBenchDbContext : DbContext
{
    public EfBenchDbContext(DbContextOptions<EfBenchDbContext> options) : base(options) { }
    public DbSet<EfBenchWidget> Widgets => Set<EfBenchWidget>();
}
