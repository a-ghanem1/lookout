using FluentAssertions;
using Lookout.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lookout.AspNetCore.Tests.Security;

public sealed class LookoutNonLoopbackTests
{
    // ── non-loopback warning ─────────────────────────────────────────────────

    [Fact]
    public async Task NonLoopback_AllowNonLoopbackFalse_LogsStartupWarning()
    {
        var logs = new List<(LogLevel Level, string Message)>();

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.WebHost.UseUrls("http://0.0.0.0:5099"); // non-loopback
        builder.Services.AddLookout(o => o.AllowNonLoopback = false);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new CaptureLoggerProvider(logs));

        await using var app = builder.Build();
        app.UseLookout();
        app.MapLookout();
        await app.StartAsync();
        await app.StopAsync();

        logs.Should().Contain(l =>
            l.Level == LogLevel.Warning &&
            l.Message.Contains("non-loopback") &&
            l.Message.Contains("0.0.0.0"));
    }

    [Fact]
    public async Task NonLoopback_AllowNonLoopbackTrue_NoWarning()
    {
        var logs = new List<(LogLevel Level, string Message)>();

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.WebHost.UseUrls("http://0.0.0.0:5098"); // non-loopback
        builder.Services.AddLookout(o => o.AllowNonLoopback = true);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new CaptureLoggerProvider(logs));

        await using var app = builder.Build();
        app.UseLookout();
        app.MapLookout();
        await app.StartAsync();
        await app.StopAsync();

        logs.Should().NotContain(l => l.Message.Contains("non-loopback"));
    }

    [Fact]
    public async Task NonLoopback_LoopbackUrl_NoWarning()
    {
        var logs = new List<(LogLevel Level, string Message)>();

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.WebHost.UseUrls("http://localhost:5097"); // loopback
        builder.Services.AddLookout(o => o.AllowNonLoopback = false);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new CaptureLoggerProvider(logs));

        await using var app = builder.Build();
        app.UseLookout();
        app.MapLookout();
        await app.StartAsync();
        await app.StopAsync();

        logs.Should().NotContain(l => l.Message.Contains("non-loopback"));
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private sealed class CaptureLoggerProvider(List<(LogLevel Level, string Message)> logs) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(logs);
        public void Dispose() { }

        private sealed class CaptureLogger(List<(LogLevel Level, string Message)> logs) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
                => logs.Add((logLevel, formatter(state, exception)));
        }
    }
}
