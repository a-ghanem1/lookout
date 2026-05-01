---
sidebar_position: 2
title: Configuration
description: Full reference for every LookoutOptions property, its type, default, and purpose.
---

# Configuration

All options are set via `AddLookout(options => { ... })` in `Program.cs`. Every option has a sane default — you only touch what you need.

```csharp
builder.Services.AddLookout(options =>
{
    options.MaxAgeHours   = 24;      // keep entries for 24 hours (default)
    options.MaxEntryCount = 50_000;  // hard cap (default)
});
```

---

## General

| Property | Type | Default | Description |
|---|---|---|---|
| `AllowInEnvironments` | `IList<string>` | `["Development"]` | Environments where Lookout is allowed to run. Lookout throws `LookoutEnvironmentException` on startup in any other environment. |
| `AllowInProduction` | `bool` | `false` | When `true`, allows Lookout to run in any environment including Production. Logs a startup warning. Prefer `AllowInEnvironments` for named non-Production environments. |
| `AllowNonLoopback` | `bool` | `false` | Suppresses the startup warning when the app is bound to a non-loopback address. |
| `StoragePath` | `string` | `%LocalAppData%/Lookout/lookout.db` | Path to the SQLite database file. |
| `MaxAgeHours` | `int` | `24` | Entries older than this many hours are pruned by the background retention service. |
| `MaxEntryCount` | `int` | `50_000` | Secondary retention cap. When exceeded after time-based pruning, oldest entries are removed first. |
| `ChannelCapacity` | `int` | `10_000` | Capacity of the in-memory channel between the request path and the background flusher. When full, the oldest entry is dropped. |
| `FlushBatchSize` | `int` | `50` | Maximum entries written to SQLite in a single transaction per flusher cycle. Higher = lower transaction overhead; lower = lower per-write latency. |
| `UserClaimType` | `string?` | `null` | Claim type used to resolve the authenticated user name. When `null`, `HttpContext.User.Identity?.Name` is used. |

---

## HTTP request capture

| Property | Type | Default | Description |
|---|---|---|---|
| `CaptureRequestBody` | `bool` | `false` | Captures inbound request bodies (content-type gated, size-capped). Opt-in to avoid buffering production traffic. |
| `CaptureResponseBody` | `bool` | `false` | Captures HTTP response bodies. Same gating as request body. |
| `MaxBodyBytes` | `int` | `65_536` (64 KiB) | Maximum bytes captured per request or response body. Larger bodies are truncated with a marker. |
| `SkipPaths` | `HashSet<string>` | `/healthz`, `/health`, `/ready`, `/favicon.ico` | Paths skipped entirely by the HTTP capture middleware. Case-insensitive. `/lookout` is always skipped. |
| `CapturedContentTypes` | `HashSet<string>` | `application/json`, `application/x-www-form-urlencoded`, `text/*` | Content types eligible for body capture. Supports `text/*` wildcard. |

---

## EF Core (`options.Ef`)

| Property | Type | Default | Description |
|---|---|---|---|
| `CaptureParameterValues` | `bool` | `true` | Captures actual SQL parameter values (after redaction). |
| `CaptureParameterTypesOnly` | `bool` | `false` | Captures only parameter type names — never values. Overrides `CaptureParameterValues`. |
| `MaxStackFrames` | `int` | `20` | Maximum user-code frames captured per query. |
| `N1DetectionMinOccurrences` | `int` | `3` | Minimum identical-shaped queries within one request before flagging as N+1. |
| `N1IgnorePatterns` | `IList<Regex>` | `[]` | SQL shape keys matching any of these patterns are excluded from N+1 detection. |

```csharp
options.Ef.N1DetectionMinOccurrences = 5;  // only flag 5+ repeated queries
options.Ef.N1IgnorePatterns.Add(new Regex("SELECT.*__EFMigrationsHistory"));
```

---

## Outbound HTTP (`options.Http`)

| Property | Type | Default | Description |
|---|---|---|---|
| `CaptureOutbound` | `bool` | `true` | Master switch for outbound HttpClient capture. |
| `CaptureOutboundRequestBody` | `bool` | `true` | Captures outbound request bodies (content-type gated). |
| `CaptureOutboundResponseBody` | `bool` | `true` | Captures outbound response bodies (content-type gated). |
| `OutboundBodyMaxBytes` | `int` | `65_536` (64 KiB) | Maximum bytes captured per outbound body. |

---

## Cache (`options.Cache`)

| Property | Type | Default | Description |
|---|---|---|---|
| `CaptureMemoryCache` | `bool` | `true` | Captures `IMemoryCache` get/set/remove operations. |
| `CaptureDistributedCache` | `bool` | `true` | Captures `IDistributedCache` get/set/refresh/remove operations. |
| `SensitiveKeyPatterns` | `IList<Regex>` | `[]` | Cache keys matching these patterns are replaced with `***` before storage. |

---

## Logging (`options.Logging`)

| Property | Type | Default | Description |
|---|---|---|---|
| `Capture` | `bool` | `true` | Master switch for log capture. |
| `MinimumLevel` | `LogLevel` | `Information` | Minimum log level captured. Raise to `Warning` to reduce noise. |
| `IgnoreCategories` | `IList<string>` | `Microsoft.*`, `System.*` | Logger category patterns dropped before recording. Trailing `*` is a prefix wildcard. |
| `MaxScopeFrames` | `int` | `5` | Maximum active scope frames captured per log entry. |

```csharp
options.Logging.MinimumLevel = LogLevel.Warning;
options.Logging.IgnoreCategories.Add("Hangfire.*");
```

---

## Exceptions (`options.Exceptions`)

| Property | Type | Default | Description |
|---|---|---|---|
| `Capture` | `bool` | `true` | Master switch for exception capture. |
| `IgnoreExceptionTypes` | `IList<string>` | `TaskCanceledException`, `OperationCanceledException` | Full CLR type names dropped before recording. Hot-reload cancellation is excluded by default. |
| `MaxStackFrames` | `int` | `20` | Maximum user-code frames captured per exception. |

---

## Dump (`options.Dump`)

| Property | Type | Default | Description |
|---|---|---|---|
| `Capture` | `bool` | `true` | Master switch for `Lookout.Dump()` entries. |
| `MaxBytes` | `int` | `65_536` | Maximum characters serialised per dump entry before truncation. |

---

## Hangfire (`options.Hangfire`)

Requires the `Lookout.Hangfire` package.

| Property | Type | Default | Description |
|---|---|---|---|
| `Capture` | `bool` | `true` | Master switch for Hangfire job capture. |
| `CaptureArguments` | `bool` | `true` | Serialises job argument values before recording. |
| `ArgumentMaxBytes` | `int` | `8_192` | Maximum bytes per job argument value before truncation. |
| `IgnoreJobTypes` | `IList<string>` | `[]` | Full CLR type names of job classes to skip (e.g. chatty heartbeat jobs). |

```csharp
options.Hangfire.IgnoreJobTypes.Add("MyApp.Jobs.HeartbeatJob");
```

---

## Redaction (`options.Redaction`)

Lookout redacts sensitive values **before writing to storage**. Matched values are replaced with `***`.

| Property | Type | Default members |
|---|---|---|
| `Headers` | `HashSet<string>` | `Authorization`, `Cookie`, `Set-Cookie`, `X-Api-Key` |
| `QueryParams` | `HashSet<string>` | `password`, `token`, `access_token`, `refresh_token`, `secret`, `api_key`, `apikey` |
| `FormFields` | `HashSet<string>` | same as QueryParams |
| `JsonFields` | `HashSet<string>` | same as QueryParams — applied recursively to captured JSON bodies |
| `SqlParameters` | `HashSet<string>` | same as QueryParams |

Add your own:

```csharp
options.Redaction.Headers.Add("X-Internal-Token");
options.Redaction.JsonFields.Add("creditCardNumber");
```

For custom redaction logic, use the `Redact` callback — it runs after built-in redactors:

```csharp
options.Redact = entry =>
{
    // Scrub a custom field from JSON body captures
    if (entry.Type == "http" && entry.Content.Contains("loyaltyCardNumber"))
    {
        var scrubbed = Regex.Replace(entry.Content, @"""loyaltyCardNumber""\s*:\s*""[^""]*""", @"""loyaltyCardNumber"":""***""");
        return entry with { Content = scrubbed };
    }
    return entry;
};
```

---

## Filtering and tagging

### Drop entries before enqueue

```csharp
// Per-entry filter: runs synchronously on the request path — keep it fast
options.Filter = entry => entry.Type != "log";  // drop all log entries
```

### Drop entries in the background flusher

```csharp
// Batch filter: runs off the request path, may be slower
options.FilterBatch = entries =>
    entries.Where(e => e.DurationMs > 5).ToList();
```

### Attach custom tags

```csharp
options.Tag = (entry, tags) =>
{
    tags["tenant"] = GetCurrentTenantId();
};
```

Tags appear as filterable chips in the dashboard.
