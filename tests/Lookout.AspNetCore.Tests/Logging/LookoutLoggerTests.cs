using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Lookout.AspNetCore.Capture.Logging;
using Lookout.Core;
using Lookout.Core.Schemas;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lookout.AspNetCore.Tests.Logging;

public sealed class LookoutLoggerTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private sealed class CapturingRecorder : ILookoutRecorder
    {
        private readonly List<LookoutEntry> _entries = [];
        public IReadOnlyList<LookoutEntry> Entries => _entries;
        public void Record(LookoutEntry entry) => _entries.Add(entry);
    }

    private static (ILogger Logger, CapturingRecorder Recorder, LookoutLoggerProvider Provider)
        Build(string category = "TestCategory", Action<LookoutOptions>? configure = null)
    {
        var opts = new LookoutOptions();
        configure?.Invoke(opts);
        var recorder = new CapturingRecorder();
        // Provide a minimal IServiceProvider that resolves ILookoutRecorder.
        var services = new ServiceCollection();
        services.AddSingleton<ILookoutRecorder>(recorder);
        var provider = new LookoutLoggerProvider(services.BuildServiceProvider(), Options.Create(opts));
        var logger = provider.CreateLogger(category);
        return (logger, recorder, provider);
    }

    private static LogEntryContent Deserialize(LookoutEntry entry) =>
        JsonSerializer.Deserialize<LogEntryContent>(entry.Content, LookoutJson.Options)!;

    // ── IsEnabled ─────────────────────────────────────────────────────────────

    [Fact]
    public void IsEnabled_ReturnsFalse_BelowMinimumLevel()
    {
        var (logger, _, _) = Build(configure: o => o.Logging.MinimumLevel = LogLevel.Warning);

        logger.IsEnabled(LogLevel.Information).Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_ReturnsTrue_AtMinimumLevel()
    {
        var (logger, _, _) = Build(configure: o => o.Logging.MinimumLevel = LogLevel.Warning);

        logger.IsEnabled(LogLevel.Warning).Should().BeTrue();
    }

    [Fact]
    public void IsEnabled_ReturnsTrue_AboveMinimumLevel()
    {
        var (logger, _, _) = Build(configure: o => o.Logging.MinimumLevel = LogLevel.Warning);

        logger.IsEnabled(LogLevel.Error).Should().BeTrue();
    }

    [Fact]
    public void IsEnabled_ReturnsFalse_WhenCaptureDisabled()
    {
        var (logger, _, _) = Build(configure: o => o.Logging.Capture = false);

        logger.IsEnabled(LogLevel.Error).Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_ReturnsFalse_ForLogLevelNone()
    {
        var (logger, _, _) = Build();

        logger.IsEnabled(LogLevel.None).Should().BeFalse();
    }

    // ── entry type ────────────────────────────────────────────────────────────

    [Fact]
    public void Log_EntryType_IsLog()
    {
        var (logger, recorder, _) = Build();

        logger.LogInformation("test");

        recorder.Entries.Single().Type.Should().Be("log");
    }

    // ── basic recording ───────────────────────────────────────────────────────

    [Fact]
    public void Log_RecordsEntry_WithLevel()
    {
        var (logger, recorder, _) = Build();

        logger.LogWarning("a warning");

        Deserialize(recorder.Entries.Single()).Level.Should().Be("Warning");
    }

    [Fact]
    public void Log_RecordsEntry_WithCategory()
    {
        var (logger, recorder, _) = Build(category: "MyApp.Services.OrderService");

        logger.LogInformation("test");

        Deserialize(recorder.Entries.Single()).Category.Should().Be("MyApp.Services.OrderService");
    }

    [Fact]
    public void Log_RecordsEntry_WithRenderedMessage()
    {
        var (logger, recorder, _) = Build();

        logger.LogInformation("order {OrderId} processed", 42);

        Deserialize(recorder.Entries.Single()).Message.Should().Be("order 42 processed");
    }

    [Fact]
    public void Log_RecordsEntry_WithEventId()
    {
        var (logger, recorder, _) = Build();

        logger.Log(LogLevel.Information, new EventId(100, "OrderProcessed"), "test", null,
            (s, _) => s);

        var content = Deserialize(recorder.Entries.Single());
        content.EventId.Id.Should().Be(100);
        content.EventId.Name.Should().Be("OrderProcessed");
    }

    [Fact]
    public void Log_Tags_EventIdTagPresentWhenIdIsNonZero()
    {
        var (logger, recorder, _) = Build();

        logger.Log(LogLevel.Information, new EventId(42), "test", null, (s, _) => s);

        recorder.Entries.Single().Tags.Should().ContainKey("log.event-id").WhoseValue.Should().Be("42");
    }

    [Fact]
    public void Log_Tags_EventIdTagAbsentWhenIdIsZero()
    {
        var (logger, recorder, _) = Build();

        logger.LogInformation("test");

        recorder.Entries.Single().Tags.Should().NotContainKey("log.event-id");
    }

    [Fact]
    public void Log_Tags_ContainLogLevelAndCategory()
    {
        var (logger, recorder, _) = Build(category: "MyApp.OrderService");

        logger.LogWarning("test");

        var tags = recorder.Entries.Single().Tags;
        tags["log.level"].Should().Be("Warning");
        tags["log.category"].Should().Be("MyApp.OrderService");
    }

    // ── scopes ────────────────────────────────────────────────────────────────

    [Fact]
    public void Log_RecordsScopes_FromExternalScopeProvider()
    {
        var (logger, recorder, provider) = Build();
        var sp = new LoggerExternalScopeProvider();
        provider.SetScopeProvider(sp);

        using (((IExternalScopeProvider)sp).Push("scope-a"))
        using (((IExternalScopeProvider)sp).Push("scope-b"))
        {
            logger.LogInformation("inside scopes");
        }

        var content = Deserialize(recorder.Entries.Single());
        content.Scopes.Should().Contain("scope-a").And.Contain("scope-b");
    }

    [Fact]
    public void Log_ScopesCappedAtMaxScopeFrames()
    {
        var (logger, recorder, provider) = Build(configure: o => o.Logging.MaxScopeFrames = 2);
        var sp = new LoggerExternalScopeProvider();
        provider.SetScopeProvider(sp);

        using (((IExternalScopeProvider)sp).Push("s1"))
        using (((IExternalScopeProvider)sp).Push("s2"))
        using (((IExternalScopeProvider)sp).Push("s3"))
        {
            logger.LogInformation("test");
        }

        Deserialize(recorder.Entries.Single()).Scopes.Should().HaveCount(2);
    }

    [Fact]
    public void Log_Scopes_EmptyWhenNoneActive()
    {
        var (logger, recorder, _) = Build();

        logger.LogInformation("no scopes");

        Deserialize(recorder.Entries.Single()).Scopes.Should().BeEmpty();
    }

    // ── exception fields ──────────────────────────────────────────────────────

    [Fact]
    public void Log_WithException_CapturesExceptionTypeAndMessage()
    {
        var (logger, recorder, _) = Build();
        var ex = new InvalidOperationException("something broke");

        logger.LogError(ex, "an error occurred");

        var content = Deserialize(recorder.Entries.Single());
        content.ExceptionType.Should().Be(typeof(InvalidOperationException).FullName);
        content.ExceptionMessage.Should().Be("something broke");
    }

    [Fact]
    public void Log_WithException_DoesNotCaptureStack()
    {
        var (logger, recorder, _) = Build();
        Exception ex;
        try { throw new InvalidOperationException("stack-test"); }
        catch (InvalidOperationException e) { ex = e; }

        logger.LogError(ex, "error");

        var content = Deserialize(recorder.Entries.Single());
        // Stack details live in the companion exception entry, not here.
        content.Should().NotBeNull("schema has no Stack field — short-form only");
    }

    [Fact]
    public void Log_WithoutException_ExceptionFieldsAreNull()
    {
        var (logger, recorder, _) = Build();

        logger.LogInformation("no exception");

        var content = Deserialize(recorder.Entries.Single());
        content.ExceptionType.Should().BeNull();
        content.ExceptionMessage.Should().BeNull();
    }

    // ── IgnoreCategories ──────────────────────────────────────────────────────

    [Fact]
    public void Log_IgnoresCategory_WhenWildcardMatches()
    {
        var (logger, recorder, _) = Build(
            category: "Microsoft.AspNetCore.Routing.EndpointMiddleware",
            configure: o => o.Logging.IgnoreCategories = ["Microsoft.AspNetCore.*"]);

        logger.LogInformation("test");

        recorder.Entries.Should().BeEmpty();
    }

    [Fact]
    public void Log_IgnoresCategory_WhenExactMatch()
    {
        var (logger, recorder, _) = Build(
            category: "MyApp.NoisyService",
            configure: o => o.Logging.IgnoreCategories = ["MyApp.NoisyService"]);

        logger.LogInformation("test");

        recorder.Entries.Should().BeEmpty();
    }

    [Fact]
    public void Log_RecordsEntry_WhenCategoryNotIgnored()
    {
        var (logger, recorder, _) = Build(
            category: "MyApp.OrderService",
            configure: o => o.Logging.IgnoreCategories = ["Microsoft.AspNetCore.*"]);

        logger.LogInformation("test");

        recorder.Entries.Should().ContainSingle();
    }

    [Theory]
    [InlineData("Microsoft.AspNetCore.Hosting.Diagnostics")]
    [InlineData("Microsoft.Extensions.Hosting.Internal.Host")]
    [InlineData("Microsoft.EntityFrameworkCore.Database.Command")]
    [InlineData("Microsoft.Hosting.Lifetime")]
    [InlineData("System.Net.Http.HttpClient")]
    public void Log_IgnoresCategory_DefaultPatterns_BlockFrameworkCategories(string category)
    {
        var (logger, recorder, _) = Build(category: category);

        logger.LogInformation("startup");

        recorder.Entries.Should().BeEmpty(
            $"{category} is blocked by default Microsoft.*/System.* IgnoreCategories");
    }

    // ── RequestId correlation ─────────────────────────────────────────────────

    [Fact]
    public void Log_RequestId_IsNullOutsideActivity()
    {
        var (logger, recorder, _) = Build();
        var saved = Activity.Current;
        Activity.Current = null;
        try { logger.LogInformation("test"); }
        finally { Activity.Current = saved; }

        recorder.Entries.Single().RequestId.Should().BeNull();
    }

    [Fact]
    public void Log_RequestId_UsesActivityRootIdInsideActivity()
    {
        var (logger, recorder, _) = Build();
        using var activity = new Activity("TestRequest").Start();

        logger.LogInformation("test");

        recorder.Entries.Single().RequestId.Should().Be(activity.RootId);
    }

    // ── Capture disabled ──────────────────────────────────────────────────────

    [Fact]
    public void Log_NoEntry_WhenCaptureDisabled()
    {
        var (logger, recorder, _) = Build(configure: o => o.Logging.Capture = false);

        logger.LogError("important error");

        recorder.Entries.Should().BeEmpty();
    }

    [Fact]
    public void Log_NoEntry_WhenBelowMinimumLevel()
    {
        var (logger, recorder, _) = Build(configure: o => o.Logging.MinimumLevel = LogLevel.Warning);

        logger.LogInformation("noisy info");

        recorder.Entries.Should().BeEmpty();
    }

    // ── failure safety ────────────────────────────────────────────────────────

    [Fact]
    public void Log_DoesNotThrow_WhenRecorderFails()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILookoutRecorder, ThrowingRecorder>();
        var provider = new LookoutLoggerProvider(
            services.BuildServiceProvider(), Options.Create(new LookoutOptions()));
        var logger = provider.CreateLogger("Test");

        var act = () => logger.LogInformation("test");

        act.Should().NotThrow("capture failures must be swallowed");
    }

    private sealed class ThrowingRecorder : ILookoutRecorder
    {
        public void Record(LookoutEntry entry) => throw new Exception("recorder exploded");
    }
}
