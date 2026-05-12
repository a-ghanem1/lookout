using FluentAssertions;
using Lookout.Core;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lookout.AspNetCore.Tests;

public sealed class LookoutStartupValidatorTests
{
    private sealed class CapturingLogger : ILogger<LookoutStartupValidator>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];
        public IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _entries.Add((logLevel, formatter(state, exception)));
        }
    }

    private static (LookoutStartupValidator Validator, CapturingLogger Logger)
        Build(IServiceCollection services)
    {
        var capturer = new CapturingLogger();
        services.AddSingleton<ILogger<LookoutStartupValidator>>(capturer);
        var sp = services.BuildServiceProvider();
        var validator = new LookoutStartupValidator(sp, capturer);
        return (validator, capturer);
    }

    // ── IMemoryCache ──────────────────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_WhenMemoryCacheRegisteredAfterLookout_LogsWarning()
    {
        var services = new ServiceCollection();
        // Simulate: AddLookout() runs first (no IMemoryCache found), then AddMemoryCache()
        services.AddMemoryCache();
        var (validator, logger) = Build(services);

        await validator.StartAsync(CancellationToken.None);

        logger.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("AddMemoryCache()") &&
            e.Message.Contains("AddLookout()"));
    }

    [Fact]
    public async Task StartAsync_WhenMemoryCacheNotRegistered_NoWarning()
    {
        var services = new ServiceCollection();
        var (validator, logger) = Build(services);

        await validator.StartAsync(CancellationToken.None);

        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_WhenMemoryCacheIsDecorated_NoWarning()
    {
        var services = new ServiceCollection();
        // Simulate correct order: AddMemoryCache() before AddLookout() — decorator is in place
        services.AddLookout();
        services.AddMemoryCache();
        // The decorator is applied during AddLookout() only if IMemoryCache exists at that time.
        // For this test, we manually wire the decorator to simulate the correct-order scenario.
        var recorder = new FakeRecorder();
        services.AddSingleton<ILookoutRecorder>(recorder);
        services.AddSingleton<IMemoryCache>(sp =>
            new Lookout.AspNetCore.Capture.Cache.LookoutMemoryCacheDecorator(
                new MemoryCache(new MemoryCacheOptions()),
                recorder,
                Microsoft.Extensions.Options.Options.Create(new LookoutOptions())));
        var (validator, logger) = Build(services);

        await validator.StartAsync(CancellationToken.None);

        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Warning &&
            e.Message.Contains("AddMemoryCache()"));
    }

    // ── IDistributedCache ─────────────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_WhenDistributedCacheRegisteredAfterLookout_LogsWarning()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        var (validator, logger) = Build(services);

        await validator.StartAsync(CancellationToken.None);

        logger.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("distributed cache"));
    }

    [Fact]
    public async Task StartAsync_WhenDistributedCacheNotRegistered_NoWarning()
    {
        var services = new ServiceCollection();
        var (validator, logger) = Build(services);

        await validator.StartAsync(CancellationToken.None);

        logger.Entries.Should().BeEmpty();
    }

    // ── StopAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task StopAsync_CompletesWithoutError()
    {
        var services = new ServiceCollection();
        var (validator, _) = Build(services);

        var act = async () => await validator.StopAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private sealed class FakeRecorder : ILookoutRecorder
    {
        public void Record(LookoutEntry entry) { }
    }
}
