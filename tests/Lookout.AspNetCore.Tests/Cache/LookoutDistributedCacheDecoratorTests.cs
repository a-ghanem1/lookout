using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Lookout.AspNetCore.Capture.Cache;
using Lookout.Core;
using Lookout.Core.Schemas;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Lookout.AspNetCore.Tests.Cache;

public sealed class LookoutDistributedCacheDecoratorTests
{
    private static (LookoutDistributedCacheDecorator decorator, List<LookoutEntry> captured, MemoryDistributedCache inner)
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

        var inner = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var decorator = new LookoutDistributedCacheDecorator(inner, recorder, options);
        return (decorator, captured, inner);
    }

    private static CacheEntryContent Deserialize(LookoutEntry entry)
    {
        var c = JsonSerializer.Deserialize<CacheEntryContent>(entry.Content, LookoutJson.Options);
        c.Should().NotBeNull();
        return c!;
    }

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    // ── Get (sync) ────────────────────────────────────────────────────────────

    [Fact]
    public void Get_Miss_RecordsGetWithHitFalse()
    {
        var (decorator, captured, _) = Build();

        var result = decorator.Get("missing-key");

        result.Should().BeNull();
        captured.Should().HaveCount(1);
        var entry = captured[0];
        entry.Type.Should().Be("cache");
        entry.Tags["cache.system"].Should().Be("distributed");
        entry.Tags["cache.provider"].Should().Be("MemoryDistributedCache");
        entry.Tags["cache.hit"].Should().Be("false");
        entry.Tags["cache.key"].Should().Be("missing-key");

        var c = Deserialize(entry);
        c.Operation.Should().Be("Get");
        c.Hit.Should().BeFalse();
        c.Key.Should().Be("missing-key");
        c.ValueBytes.Should().BeNull();
        c.ValueType.Should().BeNull();
    }

    [Fact]
    public void Get_Hit_RecordsGetWithHitTrue()
    {
        var (decorator, captured, inner) = Build();
        inner.Set("my-key", Bytes("hello"), new DistributedCacheEntryOptions());

        var result = decorator.Get("my-key");

        result.Should().NotBeNull();
        captured.Should().HaveCount(1);
        var entry = captured[0];
        entry.Tags["cache.hit"].Should().Be("true");
        entry.Tags.Should().NotContainKey("cache.value-bytes");

        var c = Deserialize(entry);
        c.Hit.Should().BeTrue();
    }

    // ── Get (async) ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_Miss_RecordsGetWithHitFalse()
    {
        var (decorator, captured, _) = Build();

        var result = await decorator.GetAsync("missing-key");

        result.Should().BeNull();
        captured.Should().HaveCount(1);
        var c = Deserialize(captured[0]);
        c.Operation.Should().Be("Get");
        c.Hit.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_Hit_RecordsGetWithHitTrue()
    {
        var (decorator, captured, inner) = Build();
        await inner.SetAsync("k", Bytes("v"), new DistributedCacheEntryOptions());

        await decorator.GetAsync("k");

        captured.Should().HaveCount(1);
        Deserialize(captured[0]).Hit.Should().BeTrue();
    }

    // ── Set (sync) ────────────────────────────────────────────────────────────

    [Fact]
    public void Set_RecordsSetWithValueBytes()
    {
        var (decorator, captured, _) = Build();
        var payload = Bytes("hello world");

        decorator.Set("data-key", payload, new DistributedCacheEntryOptions());

        captured.Should().HaveCount(1);
        var entry = captured[0];
        entry.Tags["cache.system"].Should().Be("distributed");
        entry.Tags["cache.key"].Should().Be("data-key");
        entry.Tags.Should().NotContainKey("cache.hit");
        entry.Tags["cache.value-bytes"].Should().Be(payload.Length.ToString());

        var c = Deserialize(entry);
        c.Operation.Should().Be("Set");
        c.Hit.Should().BeNull();
        c.ValueBytes.Should().Be(payload.Length);
        c.ValueType.Should().BeNull();
    }

    [Fact]
    public void Set_ValueIsActuallyStoredInInnerCache()
    {
        var (decorator, _, inner) = Build();
        var payload = Bytes("stored");

        decorator.Set("store-key", payload, new DistributedCacheEntryOptions());

        inner.Get("store-key").Should().Equal(payload);
    }

    // ── Set (async) ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SetAsync_RecordsSetWithValueBytes()
    {
        var (decorator, captured, _) = Build();
        var payload = Bytes("async value");

        await decorator.SetAsync("async-key", payload, new DistributedCacheEntryOptions());

        captured.Should().HaveCount(1);
        var c = Deserialize(captured[0]);
        c.Operation.Should().Be("Set");
        c.ValueBytes.Should().Be(payload.Length);
    }

    // ── Refresh (sync + async) ────────────────────────────────────────────────

    [Fact]
    public void Refresh_RecordsRefreshOperation()
    {
        var (decorator, captured, inner) = Build();
        inner.Set("refresh-key", Bytes("v"), new DistributedCacheEntryOptions());

        decorator.Refresh("refresh-key");

        captured.Should().HaveCount(1);
        var entry = captured[0];
        entry.Tags["cache.key"].Should().Be("refresh-key");
        entry.Tags.Should().NotContainKey("cache.hit");
        entry.Tags.Should().NotContainKey("cache.value-bytes");

        var c = Deserialize(entry);
        c.Operation.Should().Be("Refresh");
        c.Hit.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_RecordsRefreshOperation()
    {
        var (decorator, captured, inner) = Build();
        await inner.SetAsync("rk", Bytes("v"), new DistributedCacheEntryOptions());

        await decorator.RefreshAsync("rk");

        captured.Should().HaveCount(1);
        Deserialize(captured[0]).Operation.Should().Be("Refresh");
    }

    // ── Remove (sync + async) ─────────────────────────────────────────────────

    [Fact]
    public void Remove_RecordsRemoveOperation()
    {
        var (decorator, captured, inner) = Build();
        inner.Set("del-key", Bytes("v"), new DistributedCacheEntryOptions());

        decorator.Remove("del-key");

        inner.Get("del-key").Should().BeNull();
        captured.Should().HaveCount(1);
        var entry = captured[0];
        entry.Tags["cache.key"].Should().Be("del-key");
        entry.Tags.Should().NotContainKey("cache.hit");

        var c = Deserialize(entry);
        c.Operation.Should().Be("Remove");
        c.Hit.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_RecordsRemoveOperation()
    {
        var (decorator, captured, inner) = Build();
        await inner.SetAsync("rk", Bytes("v"), new DistributedCacheEntryOptions());

        await decorator.RemoveAsync("rk");

        (await inner.GetAsync("rk")).Should().BeNull();
        Deserialize(captured[0]).Operation.Should().Be("Remove");
    }

    // ── cache.provider tag ────────────────────────────────────────────────────

    [Fact]
    public void ProviderType_ReflectsInnerTypeName()
    {
        var (decorator, captured, _) = Build();

        decorator.Get("any-key");

        captured[0].Tags["cache.provider"].Should().Be("MemoryDistributedCache");
    }

    // ── Key redaction ─────────────────────────────────────────────────────────

    [Fact]
    public void SensitiveKeyPattern_RedactsMatchingKeys()
    {
        var (decorator, captured, _) = Build(o =>
            o.SensitiveKeyPatterns = [new Regex("session:", RegexOptions.IgnoreCase)]);

        decorator.Get("session:abc123");
        decorator.Get("product:99");

        Deserialize(captured[0]).Key.Should().Be("***");
        captured[0].Tags["cache.key"].Should().Be("***");

        Deserialize(captured[1]).Key.Should().Be("product:99");
        captured[1].Tags["cache.key"].Should().Be("product:99");
    }

    [Fact]
    public void SensitiveKeyPattern_RedactsSetKey()
    {
        var (decorator, captured, _) = Build(o =>
            o.SensitiveKeyPatterns = [new Regex("^secret:")]);

        decorator.Set("secret:token", Bytes("abc"), new DistributedCacheEntryOptions());

        Deserialize(captured[0]).Key.Should().Be("***");
        captured[0].Tags["cache.key"].Should().Be("***");
    }

    // ── Master switch ─────────────────────────────────────────────────────────

    [Fact]
    public void MasterSwitch_Off_DoesNotRecord()
    {
        var (decorator, captured, inner) = Build(o => o.CaptureDistributedCache = false);
        inner.Set("k", Bytes("v"), new DistributedCacheEntryOptions());

        decorator.Get("k");
        decorator.Set("k2", Bytes("v"), new DistributedCacheEntryOptions());
        decorator.Refresh("k");
        decorator.Remove("k");

        captured.Should().BeEmpty();
    }

    [Fact]
    public async Task MasterSwitch_Off_AsyncDoesNotRecord()
    {
        var (decorator, captured, inner) = Build(o => o.CaptureDistributedCache = false);
        await inner.SetAsync("k", Bytes("v"), new DistributedCacheEntryOptions());

        await decorator.GetAsync("k");
        await decorator.SetAsync("k2", Bytes("v"), new DistributedCacheEntryOptions());
        await decorator.RefreshAsync("k");
        await decorator.RemoveAsync("k");

        captured.Should().BeEmpty();
    }

    // ── Duration ──────────────────────────────────────────────────────────────

    [Fact]
    public void DurationMs_IsPopulatedForAllOperations()
    {
        var (decorator, captured, inner) = Build();
        inner.Set("k", Bytes("v"), new DistributedCacheEntryOptions());

        decorator.Get("k");
        decorator.Set("k2", Bytes("v"), new DistributedCacheEntryOptions());
        decorator.Refresh("k");
        decorator.Remove("k");

        captured.Should().HaveCount(4);
        captured.Should().OnlyContain(e => e.DurationMs >= 0);
        captured.Select(e => Deserialize(e).DurationMs).Should().OnlyContain(d => d >= 0);
    }
}
