using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Lookout.AspNetCore.Capture.Cache;
using Lookout.Core;
using Lookout.Core.Schemas;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Lookout.AspNetCore.Tests.Cache;

public sealed class LookoutMemoryCacheDecoratorTests
{
    private static (LookoutMemoryCacheDecorator decorator, List<LookoutEntry> captured, MemoryCache inner)
        Build(Action<CacheOptions>? configure = null)
    {
        var opts = new LookoutOptions();
        configure?.Invoke(opts.Cache);

        var options = Substitute.For<IOptions<LookoutOptions>>();
        options.Value.Returns(opts);

        var captured = new List<LookoutEntry>();
        var recorder = Substitute.For<ILookoutRecorder>();
        recorder.When(r => r.Record(Arg.Any<LookoutEntry>()))
            .Do(ci => captured.Add(ci.Arg<LookoutEntry>()));

        var inner = new MemoryCache(Options.Create(new MemoryCacheOptions()));
        var decorator = new LookoutMemoryCacheDecorator(inner, recorder, options);
        return (decorator, captured, inner);
    }

    private static CacheEntryContent Deserialize(LookoutEntry entry)
    {
        var c = JsonSerializer.Deserialize<CacheEntryContent>(entry.Content, LookoutJson.Options);
        c.Should().NotBeNull();
        return c!;
    }

    // ── TryGetValue ──────────────────────────────────────────────────────────

    [Fact]
    public void TryGetValue_Miss_RecordsGetWithHitFalse()
    {
        var (decorator, captured, _) = Build();

        var hit = decorator.TryGetValue("missing-key", out _);

        hit.Should().BeFalse();
        captured.Should().HaveCount(1);
        var entry = captured[0];
        entry.Type.Should().Be("cache");
        entry.Tags["cache.system"].Should().Be("memory");
        entry.Tags["cache.hit"].Should().Be("false");
        entry.Tags["cache.key"].Should().Be("missing-key");

        var c = Deserialize(entry);
        c.Operation.Should().Be("Get");
        c.Hit.Should().BeFalse();
        c.Key.Should().Be("missing-key");
    }

    [Fact]
    public void TryGetValue_Hit_RecordsGetWithHitTrueAndValueType()
    {
        var (decorator, captured, inner) = Build();
        inner.Set("my-key", "hello world");

        var hit = decorator.TryGetValue("my-key", out var value);

        hit.Should().BeTrue();
        value.Should().Be("hello world");

        captured.Should().HaveCount(1);
        var entry = captured[0];
        entry.Tags["cache.hit"].Should().Be("true");
        entry.Tags["cache.value-type"].Should().Be("System.String");

        var c = Deserialize(entry);
        c.Operation.Should().Be("Get");
        c.Hit.Should().BeTrue();
        c.ValueType.Should().Be("System.String");
    }

    // ── CreateEntry / Set ─────────────────────────────────────────────────────

    [Fact]
    public void CreateEntry_Dispose_RecordsSetWithValueType()
    {
        var (decorator, captured, _) = Build();

        using (var entry = decorator.CreateEntry("product:42"))
        {
            entry.Value = 12345;
        }

        captured.Should().HaveCount(1);
        var e = captured[0];
        e.Type.Should().Be("cache");
        e.Tags["cache.system"].Should().Be("memory");
        e.Tags.Should().NotContainKey("cache.hit");
        e.Tags["cache.key"].Should().Be("product:42");
        e.Tags["cache.value-type"].Should().Be("System.Int32");

        var c = Deserialize(e);
        c.Operation.Should().Be("Set");
        c.Hit.Should().BeNull();
        c.Key.Should().Be("product:42");
        c.ValueType.Should().Be("System.Int32");
    }

    [Fact]
    public void CreateEntry_ValueIsActuallyStoredInInnerCache()
    {
        var (decorator, _, inner) = Build();

        using (var entry = decorator.CreateEntry("greet"))
        {
            entry.Value = "hello";
        }

        inner.TryGetValue("greet", out var stored).Should().BeTrue();
        stored.Should().Be("hello");
    }

    // ── Remove ────────────────────────────────────────────────────────────────

    [Fact]
    public void Remove_RecordsRemoveOperation()
    {
        var (decorator, captured, inner) = Build();
        inner.Set("to-delete", "value");

        decorator.Remove("to-delete");

        inner.TryGetValue("to-delete", out _).Should().BeFalse();

        captured.Should().HaveCount(1);
        var entry = captured[0];
        entry.Tags["cache.system"].Should().Be("memory");
        entry.Tags["cache.key"].Should().Be("to-delete");
        entry.Tags.Should().NotContainKey("cache.hit");

        var c = Deserialize(entry);
        c.Operation.Should().Be("Remove");
        c.Hit.Should().BeNull();
        c.Key.Should().Be("to-delete");
    }

    // ── Key redaction ─────────────────────────────────────────────────────────

    [Fact]
    public void SensitiveKeyPattern_RedactsMatchingKeys()
    {
        var (decorator, captured, _) = Build(o =>
            o.SensitiveKeyPatterns = [new Regex("session:", RegexOptions.IgnoreCase)]);

        decorator.TryGetValue("session:abc123", out _);
        decorator.TryGetValue("product:99", out _);

        var sessionEntry = captured[0];
        Deserialize(sessionEntry).Key.Should().Be("***");
        sessionEntry.Tags["cache.key"].Should().Be("***");

        var productEntry = captured[1];
        Deserialize(productEntry).Key.Should().Be("product:99");
        productEntry.Tags["cache.key"].Should().Be("product:99");
    }

    [Fact]
    public void SensitiveKeyPattern_RedactsSetKey()
    {
        var (decorator, captured, _) = Build(o =>
            o.SensitiveKeyPatterns = [new Regex("^secret:")]);

        using (var entry = decorator.CreateEntry("secret:token"))
        {
            entry.Value = "abc";
        }

        Deserialize(captured[0]).Key.Should().Be("***");
        captured[0].Tags["cache.key"].Should().Be("***");
    }

    // ── Master switch ─────────────────────────────────────────────────────────

    [Fact]
    public void MasterSwitch_Off_DoesNotRecord()
    {
        var (decorator, captured, inner) = Build(o => o.CaptureMemoryCache = false);
        inner.Set("k", "v");

        decorator.TryGetValue("k", out _);
        decorator.Remove("k");
        using (var e = decorator.CreateEntry("k2")) { e.Value = 1; }

        captured.Should().BeEmpty();
    }

    // ── Duration ──────────────────────────────────────────────────────────────

    [Fact]
    public void DurationMs_IsPopulatedForAllOperations()
    {
        var (decorator, captured, inner) = Build();
        inner.Set("k", "v");

        decorator.TryGetValue("k", out _);
        using (var e = decorator.CreateEntry("k2")) { e.Value = 1; }
        decorator.Remove("k");

        captured.Should().HaveCount(3);
        captured.Should().OnlyContain(e => e.DurationMs >= 0);
        captured.Select(e => Deserialize(e).DurationMs).Should().OnlyContain(d => d >= 0);
    }
}
