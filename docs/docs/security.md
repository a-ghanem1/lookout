---
sidebar_position: 4
title: Security Model
description: How Lookout stays safe in shared dev environments — environment guards, loopback binding, CSRF protection, and data retention.
---

# Security Model

Lookout is built for dev environments and takes an explicit stance: **safe by default, with explicit opt-in for anything that relaxes the guards**.

---

## Dev-only by default

Lookout checks the current environment on startup. If the environment is not in `AllowInEnvironments` (which defaults to `["Development"]`) and `AllowInProduction` is `false`, it throws:

```
LookoutEnvironmentException: Lookout is not permitted to run in the 'Production' environment.
Set AllowInEnvironments or AllowInProduction = true to override.
```

This exception is thrown during `app.UseLookout()` — before any requests are handled — so there is no silent production exposure.

### Extending to staging

```csharp
builder.Services.AddLookout(options =>
{
    options.AllowInEnvironments = ["Development", "Staging"];
});
```

`AllowInEnvironments` fully replaces the default list — `Development` is not automatically included.

### Full opt-out (escape hatch)

```csharp
options.AllowInProduction = true;  // logs a startup warning
```

Use this only for debugging production incidents where you have no other option. Lookout logs a `LogLevel.Warning` message at startup to make the opt-in visible in your log stream.

---

## Loopback-only warning

When your app is bound to a non-loopback address (i.e. accessible to other machines on the network), Lookout logs a warning at startup:

```
Lookout is bound to a non-loopback address. The dashboard may be accessible to
other machines on this network. Set AllowNonLoopback = true to suppress this warning.
```

To suppress it when you know what you're doing (e.g. a containerised dev environment):

```csharp
options.AllowNonLoopback = true;
```

---

## CSRF protection

All mutating dashboard endpoints (Clear all, delete individual entries) use a double-submit cookie pattern:

1. On the first dashboard load, Lookout sets a `__lookout-csrf` cookie (HttpOnly: false, SameSite: Strict).
2. Every mutating request must include an `X-Lookout-Csrf-Token` header whose value matches the cookie.
3. Requests that don't include the header receive `403 Forbidden`.

This prevents cross-site request forgery from a third-party page triggering a "Clear all" wipe of your captured data. Read-only endpoints (entry listing, search) are not CSRF-protected.

---

## Data retention

Lookout keeps a bounded dataset in a local SQLite file:

| Bound | Default | Option |
|---|---|---|
| Time window | 24 hours | `options.MaxAgeHours` |
| Entry cap | 50,000 | `options.MaxEntryCount` |

The background pruning service runs every 5 minutes. When the entry cap is exceeded after time-based pruning, the oldest entries are removed first.

The default storage path is `%LocalAppData%/Lookout/lookout.db` (cross-platform). Override it:

```csharp
options.StoragePath = "/tmp/myapp.lookout";
```

---

## Redaction

Lookout redacts sensitive values **before writing to SQLite** — not in the dashboard. The redactors run in the background flusher, off the request path.

Matched values are replaced with `***`. Built-in redacted fields:

- **Request headers:** `Authorization`, `Cookie`, `Set-Cookie`, `X-Api-Key`
- **Query parameters / form fields / JSON body fields / SQL parameters:** `password`, `token`, `access_token`, `refresh_token`, `secret`, `api_key`, `apikey`

Add custom redaction targets:

```csharp
options.Redaction.Headers.Add("X-Internal-Secret");
options.Redaction.JsonFields.Add("creditCardNumber");
```

For logic-based redaction:

```csharp
options.Redact = entry =>
{
    // Return a modified entry or the original unchanged
    return entry;
};
```

---

## Summary

| Concern | Default behaviour | Override |
|---|---|---|
| Production safety | Throws `LookoutEnvironmentException` | `AllowInEnvironments` or `AllowInProduction = true` |
| Network exposure | Warns on non-loopback binding | `AllowNonLoopback = true` |
| CSRF | Double-submit cookie on mutating endpoints | Not configurable |
| Data retention | 24h window, 50k entry cap | `MaxAgeHours`, `MaxEntryCount` |
| Sensitive values | Redacted before SQLite write | `Redaction.*`, `Redact` callback |
