using FluentAssertions;
using Hangfire;
using Lookout.Core;
using Lookout.Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Lookout.AspNetCore.Tests.Hangfire;

public sealed class LookoutHangfireFilterInstallerTests : IDisposable
{
    private readonly LookoutHangfireClientFilter _clientFilter;
    private readonly LookoutHangfireServerFilter _serverFilter;

    public LookoutHangfireFilterInstallerTests()
    {
        var recorder = Substitute.For<ILookoutRecorder>();
        var options = Options.Create(new LookoutOptions());
        _clientFilter = new LookoutHangfireClientFilter(
            recorder, options, NullLogger<LookoutHangfireClientFilter>.Instance);
        _serverFilter = new LookoutHangfireServerFilter();
    }

    public void Dispose()
    {
        GlobalJobFilters.Filters.Remove(_clientFilter);
        GlobalJobFilters.Filters.Remove(_serverFilter);
    }

    private LookoutHangfireFilterInstaller BuildInstaller(
        IServiceProvider sp,
        ILogger<LookoutHangfireFilterInstaller>? logger = null)
    {
        logger ??= NullLogger<LookoutHangfireFilterInstaller>.Instance;
        return new LookoutHangfireFilterInstaller(_clientFilter, _serverFilter, sp, logger);
    }

    [Fact]
    public async Task StartAsync_AddsClientFilter_WhenRecorderRegistered()
    {
        var sp = BuildSpWithRecorder();
        var installer = BuildInstaller(sp);

        await installer.StartAsync(CancellationToken.None);

        GlobalJobFilters.Filters.Any(f => f.Instance is LookoutHangfireClientFilter)
            .Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_AddsServerFilter_WhenRecorderRegistered()
    {
        var sp = BuildSpWithRecorder();
        var installer = BuildInstaller(sp);

        await installer.StartAsync(CancellationToken.None);

        GlobalJobFilters.Filters.Any(f => f.Instance is LookoutHangfireServerFilter)
            .Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_IsIdempotent_OnDoubleCall()
    {
        var sp = BuildSpWithRecorder();
        var installer = BuildInstaller(sp);

        await installer.StartAsync(CancellationToken.None);
        await installer.StartAsync(CancellationToken.None);

        GlobalJobFilters.Filters.Count(f => f.Instance is LookoutHangfireClientFilter)
            .Should().Be(1);
        GlobalJobFilters.Filters.Count(f => f.Instance is LookoutHangfireServerFilter)
            .Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_SkipsFilters_WhenRecorderNotRegistered()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var installer = BuildInstaller(sp);

        await installer.StartAsync(CancellationToken.None);

        GlobalJobFilters.Filters.Any(f =>
                f.Instance is LookoutHangfireClientFilter or LookoutHangfireServerFilter)
            .Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_LogsWarning_WhenRecorderNotRegistered()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var logger = new CapturingLogger();
        var installer = BuildInstaller(sp, logger);

        await installer.StartAsync(CancellationToken.None);

        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning);
    }

    private static IServiceProvider BuildSpWithRecorder()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ILookoutRecorder>());
        return services.BuildServiceProvider();
    }

    private sealed class CapturingLogger : ILogger<LookoutHangfireFilterInstaller>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
