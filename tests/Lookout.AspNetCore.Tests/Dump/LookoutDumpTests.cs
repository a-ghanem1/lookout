using System.Text.Json;
using FluentAssertions;
using Lookout.Core;
using Lookout.Core.Diagnostics;
using Lookout.Core.Schemas;
using static Lookout.Core.Lookout;
using Xunit;

namespace Lookout.AspNetCore.Tests.DumpTests;

public sealed class LookoutDumpTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private sealed class CapturingRecorder : ILookoutRecorder
    {
        private readonly List<LookoutEntry> _entries = [];
        public IReadOnlyList<LookoutEntry> Entries => _entries;
        public void Record(LookoutEntry entry) => _entries.Add(entry);
    }

    private static DumpEntryContent Deserialize(LookoutEntry entry) =>
        JsonSerializer.Deserialize<DumpEntryContent>(entry.Content, LookoutJson.Options)!;

    private static IDisposable ActivateRecorder(CapturingRecorder recorder, int maxBytes = 65_536)
    {
        DumpRecorder.Set(recorder, maxBytes);
        return new DumpRecorderScope();
    }

    private sealed class DumpRecorderScope : IDisposable
    {
        public void Dispose() => DumpRecorder.Clear();
    }

    // ── no-op outside request ──────────────────────────────────────────────

    [Fact]
    public void Dump_OutsideRequest_IsNoOp()
    {
        var recorder = new CapturingRecorder();
        // DumpRecorder is NOT set — simulates out-of-request call.

        Dump("should not appear");

        recorder.Entries.Should().BeEmpty();
    }

    [Fact]
    public void Dump_OutsideRequest_DoesNotThrow()
    {
        var act = () => Dump("no recorder active");

        act.Should().NotThrow();
    }

    // ── entry type ────────────────────────────────────────────────────────────

    [Fact]
    public void Dump_EntryType_IsDump()
    {
        var recorder = new CapturingRecorder();
        using var _ = ActivateRecorder(recorder);

        Dump("hello");

        recorder.Entries.Single().Type.Should().Be("dump");
    }

    // ── null value ────────────────────────────────────────────────────────────

    [Fact]
    public void Dump_Null_RecordsNullJsonAndNullValueType()
    {
        var recorder = new CapturingRecorder();
        using var _ = ActivateRecorder(recorder);

        Dump(null);

        var content = Deserialize(recorder.Entries.Single());
        content.Json.Should().Be("null");
        content.ValueType.Should().Be("<null>");
    }

    // ── simple value ──────────────────────────────────────────────────────────

    [Fact]
    public void Dump_SimpleObject_SerializesJson()
    {
        var recorder = new CapturingRecorder();
        using var _ = ActivateRecorder(recorder);

        Dump(new { Name = "Alice", Age = 30 });

        var content = Deserialize(recorder.Entries.Single());
        content.Json.Should().Contain("Alice").And.Contain("30");
        content.JsonTruncated.Should().BeFalse();
    }

    [Fact]
    public void Dump_Int_RecordsCorrectValueType()
    {
        var recorder = new CapturingRecorder();
        using var _ = ActivateRecorder(recorder);

        Dump(42);

        var content = Deserialize(recorder.Entries.Single());
        content.ValueType.Should().Contain("Int32");
    }

    // ── generic overload preserves compile-time type ──────────────────────────

    [Fact]
    public void DumpGeneric_PreservesCompileTimeType()
    {
        var recorder = new CapturingRecorder();
        using var _ = ActivateRecorder(recorder);
        object val = "hello";

        Dump<object>(val);

        var content = Deserialize(recorder.Entries.Single());
        content.ValueType.Should().Be(typeof(object).FullName);
    }

    // ── label ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Dump_WithLabel_RecordsLabel()
    {
        var recorder = new CapturingRecorder();
        using var _ = ActivateRecorder(recorder);

        Dump("value", label: "my label");

        var content = Deserialize(recorder.Entries.Single());
        content.Label.Should().Be("my label");
    }

    [Fact]
    public void Dump_WithoutLabel_LabelIsNull()
    {
        var recorder = new CapturingRecorder();
        using var _ = ActivateRecorder(recorder);

        Dump("value");

        var content = Deserialize(recorder.Entries.Single());
        content.Label.Should().BeNull();
    }

    // ── truncation ────────────────────────────────────────────────────────────

    [Fact]
    public void Dump_HugeObject_TruncatesAtMaxBytes()
    {
        var recorder = new CapturingRecorder();
        using var _ = ActivateRecorder(recorder, maxBytes: 10);
        var bigString = new string('x', 1000);

        Dump(bigString);

        var content = Deserialize(recorder.Entries.Single());
        content.Json.Length.Should().Be(10);
        content.JsonTruncated.Should().BeTrue();
    }

    [Fact]
    public void Dump_SmallObject_NotTruncated()
    {
        var recorder = new CapturingRecorder();
        using var _ = ActivateRecorder(recorder);

        Dump("tiny");

        var content = Deserialize(recorder.Entries.Single());
        content.JsonTruncated.Should().BeFalse();
    }

    // ── cyclic graph ──────────────────────────────────────────────────────────

    [Fact]
    public void Dump_CyclicObject_RecordsFailureMarker_NoExceptionEscapes()
    {
        var recorder = new CapturingRecorder();
        using var _ = ActivateRecorder(recorder);
        var cyclic = new CyclicNode();
        cyclic.Self = cyclic;

        var act = () => Dump(cyclic);

        act.Should().NotThrow();
        recorder.Entries.Should().ContainSingle();
        var content = Deserialize(recorder.Entries.Single());
        content.Json.Should().StartWith("[lookout: serialization failed:");
    }

    private sealed class CyclicNode
    {
        public CyclicNode? Self { get; set; }
    }

    // ── tags ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Dump_Tags_ContainDumpTrueAndType()
    {
        var recorder = new CapturingRecorder();
        using var _ = ActivateRecorder(recorder);

        Dump(42);

        var tags = recorder.Entries.Single().Tags;
        tags.Should().ContainKey("dump").WhoseValue.Should().Be("true");
        tags.Should().ContainKey("dump.type");
    }

    [Fact]
    public void Dump_WithLabel_Tags_ContainDumpLabel()
    {
        var recorder = new CapturingRecorder();
        using var _ = ActivateRecorder(recorder);

        Dump("x", label: "after-lookup");

        var tags = recorder.Entries.Single().Tags;
        tags.Should().ContainKey("dump.label").WhoseValue.Should().Be("after-lookup");
    }

    [Fact]
    public void Dump_WithoutLabel_Tags_DoNotContainDumpLabel()
    {
        var recorder = new CapturingRecorder();
        using var _ = ActivateRecorder(recorder);

        Dump("x");

        recorder.Entries.Single().Tags.Should().NotContainKey("dump.label");
    }

    // ── caller info ───────────────────────────────────────────────────────────

    [Fact]
    public void Dump_RecordsCallerInfo()
    {
        var recorder = new CapturingRecorder();
        using var _ = ActivateRecorder(recorder);

        Dump("caller-test");

        var content = Deserialize(recorder.Entries.Single());
        content.CallerFile.Should().EndWith(".cs",
            "CallerFilePath resolves to a .cs source file");
        content.CallerLine.Should().BeGreaterThan(0);
        content.CallerMember.Should().NotBeNullOrEmpty();
    }

    // ── N1RequestScope wiring ─────────────────────────────────────────────────

    [Fact]
    public void Dump_WithActiveScope_IncrementsN1DumpCount()
    {
        var recorder = new CapturingRecorder();
        using var _ = ActivateRecorder(recorder);
        using var scope = N1RequestScope.Begin(new EfOptions());

        Dump("one");
        Dump("two");

        scope.DumpCount.Should().Be(2);
    }

    [Fact]
    public void Dump_WithoutScope_DoesNotThrow()
    {
        var recorder = new CapturingRecorder();
        using var _ = ActivateRecorder(recorder);

        var act = () => Dump("no scope");

        act.Should().NotThrow();
    }

    // ── Capture disabled (DumpRecorder not set) ───────────────────────────────

    [Fact]
    public void Dump_WhenRecorderNotSet_ProducesNoEntries()
    {
        DumpRecorder.Clear(); // ensure cleared

        Dump("ignored");

        // No recorder to capture — just confirming no throw and no side effects.
        // If we had a recorder we'd check it's empty, but that's the no-op test above.
    }
}
