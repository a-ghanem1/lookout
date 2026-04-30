using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Hangfire;
using Hangfire.Client;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Lookout.Core;
using Lookout.Core.Diagnostics;
using Lookout.Core.Schemas;
using Lookout.Hangfire;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Lookout.AspNetCore.Tests.Hangfire;

public sealed class LookoutHangfireClientFilterTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static LookoutHangfireClientFilter BuildFilter(
        out List<LookoutEntry> recorded,
        Action<LookoutOptions>? configure = null)
    {
        var entries = new List<LookoutEntry>();
        recorded = entries;

        var recorder = Substitute.For<ILookoutRecorder>();
        recorder.When(r => r.Record(Arg.Any<LookoutEntry>()))
                .Do(ci => entries.Add(ci.Arg<LookoutEntry>()));

        var options = new LookoutOptions();
        configure?.Invoke(options);

        return new LookoutHangfireClientFilter(
            recorder,
            Options.Create(options),
            NullLogger<LookoutHangfireClientFilter>.Instance);
    }

    private static (CreatingContext, CreatedContext) MakeContexts(
        Type jobType,
        string methodName,
        object?[] args,
        IState? state = null)
    {
        var method = jobType.GetMethod(methodName)
            ?? throw new InvalidOperationException($"Method {methodName} not found on {jobType}");
        var job = new Job(jobType, method, args);
        state ??= new EnqueuedState("default");

        // Hangfire's filter contexts are sealed with internal constructors, so we build them
        // indirectly via a real CreateContext (no storage needed for unit tests).
        var createCtx = new CreatingContext(
            new CreateContext(
                Substitute.For<global::Hangfire.JobStorage>(),
                Substitute.For<global::Hangfire.Storage.IStorageConnection>(),
                job,
                state));

        var createdCtx = new CreatedContext(
            new CreateContext(
                Substitute.For<global::Hangfire.JobStorage>(),
                Substitute.For<global::Hangfire.Storage.IStorageConnection>(),
                job,
                state),
            new BackgroundJob("job-1", job, DateTime.UtcNow),
            false, null);

        return (createCtx, createdCtx);
    }

    // sample job classes

    private sealed class SampleJob
    {
        public void Execute(string name, int count) { }
        public void RunWithSecret(string password, string data) { }
    }

    private class BaseJob { public void Run() { } }
    private sealed class DerivedJob : BaseJob { }

    private sealed class ChattyJob { public void Heartbeat() { } }

    // ── OnCreated — core fields ───────────────────────────────────────────────

    [Fact]
    public void OnCreated_RecordsJobIdAndType()
    {
        var filter = BuildFilter(out var recorded);
        var (_, created) = MakeContexts(typeof(SampleJob), nameof(SampleJob.Execute), ["hello", 1]);

        filter.OnCreated(created);

        recorded.Should().ContainSingle();
        recorded[0].Type.Should().Be("job-enqueue");
        recorded[0].Tags["job.id"].Should().Be("job-1");
        recorded[0].Tags["job.type"].Should().Contain(nameof(SampleJob));
    }

    [Fact]
    public void OnCreated_RecordsMethodName()
    {
        var filter = BuildFilter(out var recorded);
        var (_, created) = MakeContexts(typeof(SampleJob), nameof(SampleJob.Execute), ["hello", 1]);

        filter.OnCreated(created);

        recorded[0].Tags["job.method"].Should().Be(nameof(SampleJob.Execute));
        var content = Deserialize(recorded[0]);
        content.MethodName.Should().Be(nameof(SampleJob.Execute));
    }

    [Fact]
    public void OnCreated_RecordsArguments()
    {
        var filter = BuildFilter(out var recorded);
        var (_, created) = MakeContexts(typeof(SampleJob), nameof(SampleJob.Execute), ["hello", 42]);

        filter.OnCreated(created);

        var content = Deserialize(recorded[0]);
        content.Arguments.Should().HaveCount(2);
        content.Arguments[0].Name.Should().Be("name");
        content.Arguments[0].Value.Should().Contain("hello");
        content.Arguments[1].Name.Should().Be("count");
        content.Arguments[1].Value.Should().Contain("42");
    }

    [Fact]
    public void OnCreated_RecordsQueue_FromEnqueuedState()
    {
        var filter = BuildFilter(out var recorded);
        var (_, created) = MakeContexts(
            typeof(SampleJob), nameof(SampleJob.Execute), ["x", 0],
            new EnqueuedState("critical"));

        filter.OnCreated(created);

        recorded[0].Tags["job.queue"].Should().Be("critical");
        Deserialize(recorded[0]).Queue.Should().Be("critical");
    }

    [Fact]
    public void OnCreated_DefaultsQueueToDefault_WhenStateIsNotEnqueuedState()
    {
        var filter = BuildFilter(out var recorded);
        var (_, created) = MakeContexts(
            typeof(SampleJob), nameof(SampleJob.Execute), ["x", 0],
            new ScheduledState(TimeSpan.FromMinutes(5)));

        filter.OnCreated(created);

        Deserialize(recorded[0]).Queue.Should().Be("default");
    }

    [Fact]
    public void OnCreated_RecordsState_FromInitialState()
    {
        var filter = BuildFilter(out var recorded);
        var (_, created) = MakeContexts(
            typeof(SampleJob), nameof(SampleJob.Execute), ["x", 0],
            new ScheduledState(TimeSpan.FromMinutes(5)));

        filter.OnCreated(created);

        Deserialize(recorded[0]).State.Should().Be(ScheduledState.StateName);
    }

    // ── Argument redaction ────────────────────────────────────────────────────

    [Fact]
    public void OnCreated_RedactsArguments_MatchingFormFieldNames()
    {
        var filter = BuildFilter(out var recorded);
        var (_, created) = MakeContexts(
            typeof(SampleJob), nameof(SampleJob.RunWithSecret), ["secret123", "public"]);

        filter.OnCreated(created);

        var content = Deserialize(recorded[0]);
        var passwordArg = content.Arguments.First(a => a.Name == "password");
        passwordArg.Value.Should().Be("***");
        var dataArg = content.Arguments.First(a => a.Name == "data");
        dataArg.Value.Should().NotBe("***");
    }

    // ── Argument truncation ───────────────────────────────────────────────────

    [Fact]
    public void OnCreated_TruncatesArgument_ExceedingArgumentMaxBytes()
    {
        var filter = BuildFilter(out var recorded, o => o.Hangfire.ArgumentMaxBytes = 10);
        var bigString = new string('x', 200);
        var (_, created) = MakeContexts(typeof(SampleJob), nameof(SampleJob.Execute), [bigString, 1]);

        filter.OnCreated(created);

        var content = Deserialize(recorded[0]);
        content.Arguments[0].Value.Should().EndWith("[lookout:truncated]");
    }

    // ── IgnoreJobTypes ────────────────────────────────────────────────────────

    [Fact]
    public void OnCreated_DropsEntry_ForIgnoredJobType()
    {
        var filter = BuildFilter(out var recorded,
            o => o.Hangfire.IgnoreJobTypes.Add(typeof(ChattyJob).FullName!));
        var (_, created) = MakeContexts(typeof(ChattyJob), nameof(ChattyJob.Heartbeat), []);

        filter.OnCreated(created);

        recorded.Should().BeEmpty();
    }

    [Fact]
    public void OnCreated_DropsEntry_ForSubclassOfIgnoredType()
    {
        var filter = BuildFilter(out var recorded,
            o => o.Hangfire.IgnoreJobTypes.Add(typeof(BaseJob).FullName!));
        var (_, created) = MakeContexts(typeof(DerivedJob), nameof(DerivedJob.Run), []);

        filter.OnCreated(created);

        recorded.Should().BeEmpty();
    }

    // ── Recording failure swallowed ────────────────────────────────────────────

    [Fact]
    public void OnCreated_DoesNotThrow_WhenRecordingFails()
    {
        var recorder = Substitute.For<ILookoutRecorder>();
        recorder.When(r => r.Record(Arg.Any<LookoutEntry>()))
                .Do(_ => throw new InvalidOperationException("storage down"));

        var filter = new LookoutHangfireClientFilter(
            recorder,
            Options.Create(new LookoutOptions()),
            NullLogger<LookoutHangfireClientFilter>.Instance);

        var (_, created) = MakeContexts(typeof(SampleJob), nameof(SampleJob.Execute), ["x", 1]);

        var act = () => filter.OnCreated(created);
        act.Should().NotThrow();
    }

    // ── RequestId correlation ─────────────────────────────────────────────────

    [Fact]
    public void OnCreated_SetsNullRequestId_WhenNoActivity()
    {
        Activity.Current = null;
        var filter = BuildFilter(out var recorded);
        var (_, created) = MakeContexts(typeof(SampleJob), nameof(SampleJob.Execute), ["x", 1]);

        filter.OnCreated(created);

        recorded[0].RequestId.Should().BeNull();
    }

    [Fact]
    public void OnCreating_StampsEnqueueRequestIdParameter_WhenActivityPresent()
    {
        using var activity = new ActivitySource("test").StartActivity("req");
        if (activity is null)
        {
            // ActivitySource with no listeners — fall back to manual Activity
            var manualActivity = new Activity("req");
            manualActivity.Start();
            Activity.Current = manualActivity;
        }

        try
        {
            string? stamped = null;
            var recorder = Substitute.For<ILookoutRecorder>();
            var filter = new LookoutHangfireClientFilter(
                recorder,
                Options.Create(new LookoutOptions()),
                NullLogger<LookoutHangfireClientFilter>.Instance);

            var (creating, _) = MakeContexts(typeof(SampleJob), nameof(SampleJob.Execute), ["x", 1]);
            filter.OnCreating(creating);

            stamped = creating.GetJobParameter<string>(LookoutHangfireClientFilter.EnqueueRequestIdParam);
            stamped.Should().NotBeNullOrEmpty();
        }
        finally
        {
            Activity.Current?.Stop();
        }
    }

    // ── N1RequestScope counter ────────────────────────────────────────────────

    [Fact]
    public void OnCreated_IncrementsJobEnqueueCount_WhenScopeActive()
    {
        using var scope = N1RequestScope.Begin(new EfOptions());
        var filter = BuildFilter(out _);
        var (_, created) = MakeContexts(typeof(SampleJob), nameof(SampleJob.Execute), ["x", 1]);

        filter.OnCreated(created);

        scope.JobEnqueueCount.Should().Be(1);
    }

    // ── CaptureArguments=false ────────────────────────────────────────────────

    [Fact]
    public void OnCreated_RecordsEmptyArguments_WhenCaptureArgumentsFalse()
    {
        var filter = BuildFilter(out var recorded, o => o.Hangfire.CaptureArguments = false);
        var (_, created) = MakeContexts(typeof(SampleJob), nameof(SampleJob.Execute), ["hello", 1]);

        filter.OnCreated(created);

        Deserialize(recorded[0]).Arguments.Should().BeEmpty();
    }

    // ── Capture=false master switch ───────────────────────────────────────────

    [Fact]
    public void OnCreated_RecordsNothing_WhenCaptureFalse()
    {
        var filter = BuildFilter(out var recorded, o => o.Hangfire.Capture = false);
        var (_, created) = MakeContexts(typeof(SampleJob), nameof(SampleJob.Execute), ["x", 1]);

        filter.OnCreated(created);

        recorded.Should().BeEmpty();
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static JobEnqueueEntryContent Deserialize(LookoutEntry entry) =>
        JsonSerializer.Deserialize<JobEnqueueEntryContent>(entry.Content, LookoutJson.Options)!;
}
