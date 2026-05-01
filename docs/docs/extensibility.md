---
sidebar_position: 3
title: Custom Capture Points
description: Record your own events into the Lookout dashboard using ILookoutRecorder or Lookout.Dump().
---

# Custom Capture Points

Lookout exposes two extension points for recording custom events:

- **`Lookout.Dump()`** — quick object inspection, no boilerplate
- **`ILookoutRecorder`** — full control over the entry structure, for production-quality capture points

---

## `Lookout.Dump()` — quick inspection

Call `Lookout.Dump()` anywhere in your application code to ship a JSON snapshot of any value to the dashboard, correlated to the current request.

```csharp
using Lookout.Core;

// Dump any object
Lookout.Dump(order);

// Add a label so it's easy to find in the dashboard
Lookout.Dump(order, label: "order before validation");

// Generic overload preserves the compile-time type
Lookout.Dump<IPaymentService>(paymentService, label: "resolved service");
```

The dashboard shows the type name, caller file + line number, and the serialised JSON — all linked to the request that triggered it.

:::tip Safe to leave in
`Lookout.Dump()` is a **no-op** when called outside of a request context (e.g. in a background service without Lookout active). It never throws. Safe to leave behind a feature flag or environment guard.
:::

### Dump options

| `options.Dump` property | Default | Description |
|---|---|---|
| `Capture` | `true` | Master switch — set to `false` to silence all `Dump()` calls |
| `MaxBytes` | `65_536` | Serialised value truncated at this many characters |

---

## `ILookoutRecorder` — custom capture points

For structured capture (e.g. recording events from a library Lookout doesn't instrument natively), inject `ILookoutRecorder` and call `Record(LookoutEntry)`.

### `LookoutEntry` shape

```csharp
public sealed record LookoutEntry(
    Guid            Id,
    string          Type,         // discriminator shown in the dashboard
    DateTimeOffset  Timestamp,
    string?         RequestId,    // correlates to the active request
    double?         DurationMs,
    IReadOnlyDictionary<string, string> Tags,
    string          Content       // JSON payload — your schema
);
```

### Minimal example

```csharp
using System.Diagnostics;
using System.Text.Json;
using Lookout.Core;
using Microsoft.AspNetCore.Http;

public sealed class MyEventCapture(ILookoutRecorder recorder, IHttpContextAccessor http)
{
    public void RecordEvent(string eventName, object payload)
    {
        var entry = new LookoutEntry(
            Id:         Guid.NewGuid(),
            Type:       "my-event",
            Timestamp:  DateTimeOffset.UtcNow,
            RequestId:  Activity.Current?.RootId,  // correlates to current request
            DurationMs: null,
            Tags:       new Dictionary<string, string> { ["event"] = eventName },
            Content:    JsonSerializer.Serialize(payload));

        recorder.Record(entry);  // fire-and-forget, never blocks
    }
}
```

Register in DI:

```csharp
builder.Services.AddScoped<MyEventCapture>();
```

### Full example — outbound gRPC capture

This is how you'd build a capture point for a library Lookout doesn't instrument yet:

```csharp
using System.Diagnostics;
using System.Text.Json;
using Lookout.Core;
using Grpc.Core.Interceptors;
using Grpc.Core;

public sealed class LookoutGrpcInterceptor(ILookoutRecorder recorder) : Interceptor
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var sw = Stopwatch.StartNew();
        var call = continuation(request, context);

        _ = RecordAsync(call, context.Method.FullName, sw);
        return call;
    }

    private async Task RecordAsync<TResponse>(
        AsyncUnaryCall<TResponse> call,
        string method,
        Stopwatch sw)
    {
        StatusCode status;
        try
        {
            await call.ResponseAsync.ConfigureAwait(false);
            status = call.GetStatus().StatusCode;
        }
        catch
        {
            status = StatusCode.Unknown;
        }

        sw.Stop();

        recorder.Record(new LookoutEntry(
            Id:         Guid.NewGuid(),
            Type:       "grpc-out",
            Timestamp:  DateTimeOffset.UtcNow,
            RequestId:  Activity.Current?.RootId,
            DurationMs: sw.Elapsed.TotalMilliseconds,
            Tags:       new Dictionary<string, string>
            {
                ["grpc.method"] = method,
                ["grpc.status"] = status.ToString(),
            },
            Content: JsonSerializer.Serialize(new { method, status = status.ToString() })));
    }
}
```

### Built-in type discriminators

Lookout's built-in capture points use these `Type` strings — avoid colliding with them:

| Type | Source |
|---|---|
| `http` | Inbound HTTP request |
| `ef` | EF Core query |
| `sql` | Raw ADO.NET / Dapper query |
| `http-out` | Outbound HttpClient call |
| `cache` | IMemoryCache / IDistributedCache |
| `log` | ILogger output |
| `exception` | Handled / unhandled exception |
| `dump` | `Lookout.Dump()` |
| `job-enqueue` | Hangfire job enqueued |
| `job-execution` | Hangfire job executed |

Choose a distinct prefix for your own types (e.g. `grpc-out`, `rabbitmq-publish`).

---

## Custom filter and tag callbacks

If you only need to drop or annotate entries without building a full capture point, use the callbacks on `LookoutOptions` (see [Configuration → Filtering and tagging](./configuration#filtering-and-tagging)).
