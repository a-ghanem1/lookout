---
sidebar_position: 1
title: Quickstart
description: Install Lookout and get your first request captured in under 2 minutes.
---

# Quickstart

Get your first request captured in under 2 minutes — no database setup, no config file.

## Prerequisites

- .NET 8 or .NET 10 SDK
- An ASP.NET Core web project (WebAPI, MVC, or minimal API)

Don't have one? Create a throwaway:

```bash
dotnet new webapi -n MyApp && cd MyApp
```

## 1. Install

```bash
dotnet add package Lookout.AspNetCore
```

If your project uses **EF Core**, also install the EF integration package in the project that
contains your `DbContext`:

```bash
dotnet add package Lookout.EntityFrameworkCore
```

For Hangfire job capture (optional):

```bash
dotnet add package Lookout.Hangfire
```

## 2. Wire up

### Program.cs — three lines

```csharp
// 1. Register services — call AFTER AddMemoryCache / AddDistributedMemoryCache
builder.Services.AddLookout();

// 2. Add middleware — call BEFORE UseRouting
app.UseLookout();

// 3. Mount the dashboard at /lookout
app.MapLookout();
```

:::tip Order matters
`AddLookout()` must come **after** `AddMemoryCache()` / `AddDistributedMemoryCache()` (if used)
so the cache decorator can wrap the registered cache. If no cache is registered yet, cache capture
is silently skipped.

`UseLookout()` must come **before** `UseRouting()` and any endpoint middleware so it captures the
full request lifecycle.
:::

### EF Core — one extra step per DbContext

EF Core query capture is not automatic. After installing `Lookout.EntityFrameworkCore`:

**Step 1** — call `AddEntityFrameworkCore()` alongside `AddLookout()`:

```csharp
builder.Services.AddLookout();
builder.Services.AddEntityFrameworkCore(); // from Lookout.EntityFrameworkCore namespace
```

**Step 2** — add `.UseLookout(sp)` inside every `AddDbContext` call you want to instrument:

```csharp
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseSqlServer(connectionString)
           .UseLookout(sp);   // <-- adds the Lookout DbCommandInterceptor
});
```

The `sp` overload of `AddDbContext` gives you the service provider so Lookout can resolve its
singleton interceptor. If you use `AddDbContextFactory`, the same `.UseLookout(sp)` call applies
inside the factory delegate.

:::info Multiple DbContexts
Call `.UseLookout(sp)` in every `AddDbContext` call you want to instrument. Each context is
independent.
:::

## 3. Run

```bash
dotnet run
```

Open the Lookout dashboard in your browser. The port is printed by `dotnet run`:

```
Now listening on: https://localhost:5001
```

So navigate to:

```
https://localhost:5001/lookout
```

Hit any endpoint in your app. The request row appears in the dashboard within milliseconds.

## What you see

The request list shows one row per captured HTTP request:

| Column | What it shows |
|---|---|
| Method + path | `GET /api/orders` |
| Status | HTTP response code |
| Duration | End-to-end time in ms |
| **db** badge | Number of EF / SQL queries |
| **http** badge | Number of outbound HTTP calls |
| **cache** badge | Cache hits + misses |
| **log** badge | Log entries scoped to this request |
| **exc** badge | Unhandled exceptions |

Click any row to open the request detail — SQL text with parameters, log output, EF stack traces, N+1 warnings.

## Your first N+1

If your code loads a collection then queries each item in a loop, Lookout flags it automatically:

```csharp
// This produces one query per order — a classic N+1
var orders = await db.Orders.ToListAsync();
foreach (var order in orders)
{
    var items = await db.OrderItems
        .Where(i => i.OrderId == order.Id)
        .ToListAsync();
}
```

The dashboard shows a warning banner for that request:

```
N+1 detected — 12 identical queries. Stack trace: OrderService.cs:47
```

The banner groups all identical SQL shapes, shows a count, and links the stack frame where the repeated query originated.

## Next steps

- [Configuration](./configuration) — body capture, retention window, redaction
- [Extensibility](./extensibility) — record custom events with `ILookoutRecorder` or `Lookout.Dump()`
- [Security model](./security) — understand the dev-only default and how to extend it
- [Troubleshooting](./troubleshooting) — ABP, Serilog, Npgsql, and other framework-specific notes
