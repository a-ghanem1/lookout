using System.Net;
using FluentAssertions;
using Lookout.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lookout.AspNetCore.Tests;

public sealed class DevOnlyEnforcementTests
{
    [Fact]
    public async Task Development_AppStartsAndLookoutIsReachable()
    {
        await using var app = BuildTestApp("Development");
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/lookout");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void Production_NoOverrides_ThrowsOnUseLookout()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Services.AddLookout();
        using var app = builder.Build();

        var act = () => app.UseLookout();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'Production'*");
    }

    [Fact]
    public async Task Staging_InAllowList_AppStartsAndLookoutIsReachable()
    {
        await using var app = BuildTestApp("Staging", o =>
            o.AllowInEnvironments = ["Development", "Staging"]);
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/lookout");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void Production_AllowInProductionTrue_DoesNotThrowAndLogsWarning()
    {
        var logs = new List<(LogLevel Level, string Message)>();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Services.AddLookout(o => o.AllowInProduction = true);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new CaptureLoggerProvider(logs));
        using var app = builder.Build();

        var act = () => app.UseLookout();
        act.Should().NotThrow();

        logs.Should().Contain(l => l.Level == LogLevel.Warning && l.Message.Contains("AllowInProduction"));
    }

    private static WebApplication BuildTestApp(string environment, Action<LookoutOptions>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Services.AddLookout(configure);
        var app = builder.Build();
        app.UseLookout();
        app.MapLookout();
        return app;
    }

    private sealed class CaptureLoggerProvider(List<(LogLevel Level, string Message)> logs) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(logs);
        public void Dispose() { }

        private sealed class CaptureLogger(List<(LogLevel Level, string Message)> logs) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => logs.Add((logLevel, formatter(state, exception)));
        }
    }
}
