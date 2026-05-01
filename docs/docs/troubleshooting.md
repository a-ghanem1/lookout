---
sidebar_position: 5
title: Troubleshooting
description: Solutions to the five most common Lookout setup issues.
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

**Cause (most common):** `AddLookout()` was called *after* `AddDbContext()`. The interceptor must register before the DbContext is built.

**Fix:**

```csharp
// CORRECT — Lookout before DbContext
builder.Services.AddLookout();
builder.Services.AddDbContext<AppDbContext>(...);
```

If you use `AddDbContextFactory` or manually create `DbContextOptions`, make sure you call `AddLookout()` first and that the interceptor is registered in the options builder. Lookout registers its interceptor via a `DbContextOptionsExtension` — it needs to observe the `AddDbContext` call.

**Also check:**
- You are using EF Core (not raw ADO.NET or Dapper only). Raw SQL via ADO.NET appears in the `sql` section, not `ef`.
- The database provider is supported (SQL Server, SQLite, PostgreSQL via Npgsql — all use standard `DbCommandInterceptor`).

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
app.UseRouting();
app.UseLookout();   // after UseRouting
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
