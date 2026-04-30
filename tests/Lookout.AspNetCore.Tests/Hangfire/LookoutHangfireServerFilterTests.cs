using System.Text.Json;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.Server;
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

public sealed class LookoutHangfireServerFilterTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static LookoutHangfireServerFilter BuildFilter(
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

        return new LookoutHangfireServerFilter(
            recorder,
            Options.Create(options),
            NullLogger<LookoutHangfireServerFilter>.Instance);
    }

    private sealed class SampleJob
    {
        public void Execute() { }
    }

    private sealed class ChattyJob
    {
        public void Heartbeat() { }
    }

    private class BaseJob { public void Run() { } }
    private sealed class DerivedJob : BaseJob { }

    private static (PerformingContext performing, PerformedContext performed) MakeServerContexts(
        Type jobType,
        string methodName,
        string? enqueueRequestId = null,
        Exception? exception = null)
    {
        var method = jobType.GetMethod(methodName)
            ?? throw new InvalidOperationException($"Method {methodName} not found on {jobType}");
        var job = new Job(jobType, method, Array.Empty<object>());
        var backgroundJob = new BackgroundJob("job-1", job, DateTime.UtcNow);

        var connection = Substitute.For<IStorageConnection>();
        if (enqueueRequestId is not null)
        {
            // Hangfire uses Newtonsoft.Json internally for GetJobParameter<T>,
            // so the stored raw value must be a JSON-encoded string.
            connection.GetJobParameter("job-1", LookoutHangfireClientFilter.EnqueueRequestIdParam)
                      .Returns($"\"{enqueueRequestId}\"");
        }

        var performCtx = new PerformContext(
            Substitute.For<JobStorage>(),
            connection,
            backgroundJob,
            Substitute.For<IJobCancellationToken>());

        return (
            new PerformingContext(performCtx),
            new PerformedContext(performCtx, null, false, exception));
    }

    private static JobExecutionEntryContent Deserialize(LookoutEntry entry) =>
        JsonSerializer.Deserialize<JobExecutionEntryContent>(entry.Content, LookoutJson.Options)!;

    // ── OnPerformed — core fields ─────────────────────────────────────────────

    [Fact]
    public void OnPerformed_RecordsJobIdAndType()
    {
        var filter = BuildFilter(out var recorded);
        var (performing, performed) = MakeServerContexts(typeof(SampleJob), nameof(SampleJob.Execute));

        filter.OnPerforming(performing);
        filter.OnPerformed(performed);

        recorded.Should().ContainSingle();
        recorded[0].Type.Should().Be("job-execution");
        recorded[0].Tags["job.id"].Should().Be("job-1");
        recorded[0].Tags["job.type"].Should().Contain(nameof(SampleJob));
    }

    [Fact]
    public void OnPerformed_RecordsMethodName()
    {
        var filter = BuildFilter(out var recorded);
        var (performing, performed) = MakeServerContexts(typeof(SampleJob), nameof(SampleJob.Execute));

        filter.OnPerforming(performing);
        filter.OnPerformed(performed);

        recorded[0].Tags["job.method"].Should().Be(nameof(SampleJob.Execute));
        Deserialize(recorded[0]).MethodName.Should().Be(nameof(SampleJob.Execute));
    }

    [Fact]
    public void OnPerformed_RecordsState_WhenSucceeded()
    {
        var filter = BuildFilter(out var recorded);
        var (performing, performed) = MakeServerContexts(typeof(SampleJob), nameof(SampleJob.Execute));

        filter.OnPerforming(performing);
        filter.OnPerformed(performed);

        recorded[0].Tags["job.state"].Should().Be("Succeeded");
        Deserialize(recorded[0]).State.Should().Be("Succeeded");
    }

    [Fact]
    public void OnPerformed_RecordsState_WhenFailed()
    {
        var filter = BuildFilter(out var recorded);
        var ex = new InvalidOperationException("boom");
        var (performing, performed) = MakeServerContexts(
            typeof(SampleJob), nameof(SampleJob.Execute), exception: ex);

        filter.OnPerforming(performing);
        filter.OnPerformed(performed);

        recorded[0].Tags["job.state"].Should().Be("Failed");
        var content = Deserialize(recorded[0]);
        content.State.Should().Be("Failed");
        content.ErrorType.Should().Contain(nameof(InvalidOperationException));
        content.ErrorMessage.Should().Be("boom");
    }

    [Fact]
    public void OnPerformed_SetsEnqueueRequestId_FromJobParameter()
    {
        var filter = BuildFilter(out var recorded);
        var (performing, performed) = MakeServerContexts(
            typeof(SampleJob), nameof(SampleJob.Execute),
            enqueueRequestId: "root-abc-123");

        filter.OnPerforming(performing);
        filter.OnPerformed(performed);

        recorded[0].RequestId.Should().Be("root-abc-123");
        recorded[0].Tags["job.enqueue.request-id"].Should().Be("root-abc-123");
        Deserialize(recorded[0]).EnqueueRequestId.Should().Be("root-abc-123");
    }

    [Fact]
    public void OnPerformed_NullRequestId_WhenNoEnqueueParameter()
    {
        var filter = BuildFilter(out var recorded);
        var (performing, performed) = MakeServerContexts(
            typeof(SampleJob), nameof(SampleJob.Execute));

        filter.OnPerforming(performing);
        filter.OnPerformed(performed);

        recorded[0].RequestId.Should().BeNull();
        recorded[0].Tags.Should().NotContainKey("job.enqueue.request-id");
        Deserialize(recorded[0]).EnqueueRequestId.Should().BeNull();
    }

    // ── N1RequestScope lifecycle ──────────────────────────────────────────────

    [Fact]
    public void OnPerforming_BeginsN1Scope()
    {
        var filter = BuildFilter(out _);
        var (performing, performed) = MakeServerContexts(typeof(SampleJob), nameof(SampleJob.Execute));

        N1RequestScope.Current.Should().BeNull();
        filter.OnPerforming(performing);
        try
        {
            N1RequestScope.Current.Should().NotBeNull();
        }
        finally
        {
            filter.OnPerformed(performed);
        }
    }

    [Fact]
    public void OnPerformed_DisposesN1Scope()
    {
        var filter = BuildFilter(out _);
        var (performing, performed) = MakeServerContexts(typeof(SampleJob), nameof(SampleJob.Execute));

        filter.OnPerforming(performing);
        filter.OnPerformed(performed);

        N1RequestScope.Current.Should().BeNull();
    }

    // ── IgnoreJobTypes ────────────────────────────────────────────────────────

    [Fact]
    public void OnPerformed_DropsEntry_ForIgnoredJobType()
    {
        var filter = BuildFilter(out var recorded,
            o => o.Hangfire.IgnoreJobTypes.Add(typeof(ChattyJob).FullName!));
        var (performing, performed) = MakeServerContexts(typeof(ChattyJob), nameof(ChattyJob.Heartbeat));

        filter.OnPerforming(performing);
        filter.OnPerformed(performed);

        recorded.Should().BeEmpty();
    }

    [Fact]
    public void OnPerformed_DropsEntry_ForSubclassOfIgnoredType()
    {
        var filter = BuildFilter(out var recorded,
            o => o.Hangfire.IgnoreJobTypes.Add(typeof(BaseJob).FullName!));
        var (performing, performed) = MakeServerContexts(typeof(DerivedJob), nameof(DerivedJob.Run));

        filter.OnPerforming(performing);
        filter.OnPerformed(performed);

        recorded.Should().BeEmpty();
    }

    // ── Failure swallowed ─────────────────────────────────────────────────────

    [Fact]
    public void OnPerformed_DoesNotThrow_WhenRecordingFails()
    {
        var recorder = Substitute.For<ILookoutRecorder>();
        recorder.When(r => r.Record(Arg.Any<LookoutEntry>()))
                .Do(_ => throw new InvalidOperationException("storage down"));

        var filter = new LookoutHangfireServerFilter(
            recorder,
            Options.Create(new LookoutOptions()),
            NullLogger<LookoutHangfireServerFilter>.Instance);

        var (performing, performed) = MakeServerContexts(typeof(SampleJob), nameof(SampleJob.Execute));

        var act = () =>
        {
            filter.OnPerforming(performing);
            filter.OnPerformed(performed);
        };
        act.Should().NotThrow();
    }

    // ── Capture=false master switch ───────────────────────────────────────────

    [Fact]
    public void OnPerformed_RecordsNothing_WhenCaptureFalse()
    {
        var filter = BuildFilter(out var recorded, o => o.Hangfire.Capture = false);
        var (performing, performed) = MakeServerContexts(typeof(SampleJob), nameof(SampleJob.Execute));

        filter.OnPerforming(performing);
        filter.OnPerformed(performed);

        recorded.Should().BeEmpty();
    }

    [Fact]
    public void OnPerforming_DoesNotBeginScope_WhenCaptureFalse()
    {
        var filter = BuildFilter(out _, o => o.Hangfire.Capture = false);
        var (performing, performed) = MakeServerContexts(typeof(SampleJob), nameof(SampleJob.Execute));

        filter.OnPerforming(performing);

        N1RequestScope.Current.Should().BeNull();
    }
}
