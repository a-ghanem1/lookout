---
sidebar_position: 5
title: Troubleshooting
description: Solutions to common Lookout setup issues, including framework-specific gotchas.
---

# Troubleshooting

---

## "Lookout throws at startup"

**Error:**

```
LookoutEnvironmentException: Lookout is not permitted to run in the 'Production' environment.
```

**Cause:** The current `ASPNETCORE_ENVIRONMENT` is not in `AllowInEnvironments` (which defaults to `["Development"]`).

**Fix:**

```csharp
// Allow a specific environment
options.AllowInEnvironments = ["Development", "Staging"];

// Or allow any environment (escape hatch — logs a startup warning)
options.AllowInProduction = true;
```

Check your environment variable:

```bash
echo $ASPNETCORE_ENVIRONMENT     # macOS / Linux
$env:ASPNETCORE_ENVIRONMENT      # PowerShell
```

---

## "EF queries are not appearing"

**Symptom:** The dashboard loads, HTTP entries appear, but the `db` badge is always 0.

**Cause:** EF Core capture requires the separate `Lookout.EntityFrameworkCore` package and
explicit wiring. It is **not** automatic from `Lookout.AspNetCore` alone.

**Fix:** Follow the [Quickstart → EF Core setup](./quickstart#ef-core--one-extra-step-per-dbcontext). The short version:

1. `dotnet add package Lookout.EntityFrameworkCore` (in the project containing your `DbContext`)
2. Call `builder.Services.AddLookoutEntityFrameworkCore()` next to `AddLookout()`
3. Add `.UseLookout(sp)` inside every `AddDbContext` call you want to instrument

**Also check:**
- You are using EF Core (not raw ADO.NET or Dapper only). Raw SQL via ADO.NET appears in the `sql` section, not `ef`.
- The database provider is supported. `DbCommandInterceptor` works with any EF Core provider (SQL Server, PostgreSQL/Npgsql, SQLite, etc.).

---

## "PostgreSQL / Npgsql raw queries not appearing"

**Symptom:** You use Npgsql directly (raw `NpgsqlCommand`, Dapper) without EF Core, and no `sql`
entries appear.

**Cause:** The built-in ADO.NET diagnostic subscriber only listens to
`SqlClientDiagnosticListener` (SQL Server). Npgsql emits events via its own `ActivitySource`
(name `"Npgsql"`), which a separate subscriber handles.

**Fix:** No additional package is required. `Lookout.AspNetCore` includes a Npgsql
`ActivitySource` subscriber that activates automatically **unless** `Lookout.EntityFrameworkCore`
is installed (in which case the EF interceptor takes over and the ActivitySource subscriber is
suppressed to avoid double-capture).

If you install `Lookout.EntityFrameworkCore` for EF queries **and** also use raw `NpgsqlCommand`
in the same app, the raw Npgsql queries will not appear in Lookout — this is a known v1
limitation. Migrate the raw queries to EF Core, or [open an issue](https://github.com/a-ghanem1/Lookout/issues) to request mixed-mode capture.

---

## "Cache hit/miss not appearing" {#cache-hitmiss-not-appearing}

**Symptom:** The dashboard shows HTTP requests but the `cache` badge is always 0.

**Cause:** `AddLookout()` decorates `IMemoryCache` and `IDistributedCache` by wrapping whatever
is already registered at call time. If `AddLookout()` runs before those registrations exist, the
decorator silently no-ops.

**Fix:** Call `AddLookout()` **after** `AddMemoryCache()` / `AddDistributedMemoryCache()`:

```csharp
builder.Services.AddMemoryCache();           // must come first
builder.Services.AddDistributedMemoryCache(); // or Redis, etc.
builder.Services.AddLookout();               // wraps the cache registrations above
```

**Framework-specific note (ABP, Orchard Core, etc.):**

When a framework registers its services inside modules that run during `AddApplicationAsync()` or
equivalent, the cache registrations happen after your top-level `AddLookout()` call. Move
`AddLookout()` to after the framework initialisation:

```csharp
await builder.AddApplicationAsync<YourModule>(); // ABP: modules register caches here
builder.Services.AddLookout();                   // now wraps the module-registered caches
```

---

## "Logs not appearing when using Serilog"

**Symptom:** Log entries are written to the Serilog output (console, file, Seq) but never appear
in the Lookout dashboard.

**Cause:** `UseSerilog()` replaces the entire `ILoggerFactory` with Serilog's own implementation.
Lookout registers `LookoutLoggerProvider` as an `ILoggerProvider` in DI, but Serilog's
`ILoggerFactory` does not resolve providers from DI — it only routes through its own sink
pipeline. As a result, `LookoutLoggerProvider` never receives log events.

**Fix:** Add `.ReadFrom.Services(services)` to your Serilog configuration. Note the extra `services` parameter in the `UseSerilog` overload:

```csharp
// Before — Lookout logs do not appear:
.UseSerilog((context, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration);
})

// After — Lookout logs forwarded via DI providers:
.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services);  // key addition — routes events to all ILoggerProvider sinks
})
```

`.ReadFrom.Services(services)` causes Serilog to forward events to all `ILoggerProvider`
instances registered in DI, including `LookoutLoggerProvider`. This is the standard bridge between
Serilog and the Microsoft logging ecosystem and requires no extra package.

---

## "Using ABP Framework (module-based DI)"

ABP registers its services, DbContexts, caches, and HttpClients inside ABP module
`ConfigureServices` methods, which run during `AddApplicationAsync()`. This affects Lookout's
registration in two ways:

### Cache capture — move AddLookout() after AddApplicationAsync()

```csharp
// Program.cs
await builder.AddApplicationAsync<YourHostModule>(); // ABP modules run here
builder.Services.AddLookout(options =>               // now sees the registered caches
{
    // configure as needed — defaults capture request/response bodies, EF, HTTP, cache, etc.
});
```

### EF Core capture — wire the interceptor in the EF module

Install `Lookout.EntityFrameworkCore` in the project containing your `DbContext` (e.g.
`YourApp.EntityFrameworkCore`), then wire the interceptor via `Configure<AbpDbContextOptions>`
inside your EF module:

```csharp
// YourApp.EntityFrameworkCore / YourAppEntityFrameworkCoreModule.cs
public override void ConfigureServices(ServiceConfigurationContext context)
{
    context.Services.AddLookoutEntityFrameworkCore(); // registers the Lookout interceptor singleton

    Configure<AbpDbContextOptions>(options =>
    {
        options.PreConfigure<YourAppDbContext>((sp, builder) =>
            builder.UseLookout(sp));            // wires the interceptor into this DbContext

        options.UseNpgsql();                   // or UseSqlServer(), etc.
    });
}
```

### HttpClient capture

Outbound `HttpClient` capture works automatically. ABP registers typed/named clients via
`IHttpClientFactory`, and Lookout's `ConfigureAll<HttpClientFactoryOptions>` hooks into every
client regardless of registration order.

---

## "N+1 not detected"

**Symptom:** You know there is a repeated query loop but no N+1 banner appears.

**Cause 1:** The number of repeated queries is below `N1DetectionMinOccurrences` (default 3).

```csharp
options.Ef.N1DetectionMinOccurrences = 2;  // flag 2+ occurrences
```

**Cause 2:** The queries differ slightly after normalisation (parameter counts, schema names). Lookout normalises SQL shapes by replacing parameter values — but structural differences (e.g. different column lists) produce different shapes.

**Cause 3:** Raw ADO.NET / Dapper queries are tracked separately in the `sql` section and are **not** included in N+1 detection. Only queries captured via the EF Core interceptor participate.

---

## "Dashboard returns 404"

**Symptom:** Navigating to `/lookout` returns a 404.

**Cause:** `MapLookout()` was not called, or was called before `UseRouting()` set up the endpoint routing pipeline.

**Fix:**

```csharp
app.UseLookout();   // before UseRouting — captures the full request lifecycle
app.UseRouting();
app.MapLookout();   // register the endpoint
app.MapControllers();
```

In minimal API projects (which don't call `UseRouting()` explicitly), `MapLookout()` is sufficient — the framework adds routing implicitly.

Also verify the dashboard is mounted at the path you're visiting. The default path is `/lookout`. If you've customised it, check the `MapLookout()` call.

---

## "`__lookout-csrf` cookie missing / 403 on Clear all"

**Symptom:** Clicking "Clear all" in the dashboard returns `403 Forbidden` or nothing happens.

**Cause:** The CSRF double-submit cookie (`__lookout-csrf`) is missing or its value doesn't match the `X-Lookout-Csrf-Token` request header.

**Common triggers:**

1. **Cross-origin dev proxy.** If the dashboard is served through a proxy that changes the `Origin` or strips cookies, the CSRF cookie may not reach the browser from the same origin. Access the dashboard directly at `https://localhost:{port}/lookout` rather than through a proxy.

2. **Browser security settings.** Some browsers block third-party cookies or cookies on `localhost` in certain configurations. Check the browser console for cookie-related warnings.

3. **`SameSite` strictness.** The cookie is set with `SameSite=Strict`. If your dev setup routes the dashboard request across a subdomain or port boundary from the page making the request, the cookie is suppressed.

**Quick check:** Open DevTools → Application → Cookies → `localhost`. Confirm `__lookout-csrf` is present. If it's missing, the issue is cookie delivery; if it's present but 403 persists, check that the JS is reading and forwarding it as a header.

---

## "Hangfire recurring job throws at startup"

**Error:**

```
InvalidOperationException: Current JobStorage instance has not been initialized yet.
```

**Cause:** `RecurringJob.AddOrUpdate()` (Hangfire's static API) was called before the Hangfire server has finished initialising — typically in a `IHostedService.StartAsync` or early startup code.

**Fix:** Use `IRecurringJobManager` from DI instead of the static API:

```csharp
// ❌ Static API — throws if called before Hangfire server is ready
RecurringJob.AddOrUpdate("my-job", () => DoWork(), Cron.Daily);

// ✅ DI — safe at any point after the container is built
public class MyStartupService(IRecurringJobManager jobs) : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        jobs.AddOrUpdate("my-job", () => DoWork(), Cron.Daily);
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

Register the startup service after `AddHangfireServer()`:

```csharp
builder.Services.AddHangfire(...);
builder.Services.AddHangfireServer();
builder.Services.AddHostedService<MyStartupService>();
```

---

**Still stuck?** Check the [Security Model](./security) for environment and startup guards, or [open an issue on GitHub](https://github.com/a-ghanem1/Lookout/issues).
