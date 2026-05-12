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

If your project uses **EF Core 8 or later**, also install the EF integration package in the project
that contains your `DbContext`:

```bash
dotnet add package Lookout.EntityFrameworkCore
```

:::note Clean Architecture solutions
In a Clean Architecture solution, install `Lookout.EntityFrameworkCore` in your **Infrastructure
project** — that is where `AddDbContext` lives. `Lookout.AspNetCore` goes only in the Web host
project. The two packages are independent; you do not need to reference `Lookout.AspNetCore` from
Infrastructure.
:::

For Hangfire job capture (optional):

```bash
dotnet add package Lookout.Hangfire
```

:::caution Recurring jobs — use DI, not the static API
Use `IRecurringJobManager` from DI to schedule recurring jobs at startup.
`RecurringJob.AddOrUpdate()` (static API) throws `InvalidOperationException` if called before
the Hangfire server is initialized.
:::

## 2. Wire up

### Program.cs — three lines

```csharp
// 1. Register services — call AFTER AddMemoryCache / AddDistributedMemoryCache
builder.Services.AddLookout();

// 2. Add middleware — call BEFORE UseRouting
app.UseLookout();

// 3. Mount the dashboard as a route on your app at /lookout (no separate process)
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

**Step 1** — call `AddLookoutEntityFrameworkCore()` in the project where your DbContext is
registered (the Web host in a simple project; the Infrastructure project in Clean Architecture):

```csharp
builder.Services.AddLookout();
builder.Services.AddLookoutEntityFrameworkCore(); // from Lookout.EntityFrameworkCore namespace
```

**Step 2** — add `.UseLookout(sp)` inside every `AddDbContext` call you want to instrument.
Note that `.UseLookout(sp)` is called inside `AddDbContext`, not in `Program.cs`:

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

The dashboard lives at the `/lookout` route on **your app's** host and port — there is no separate process. The port is printed by `dotnet run`:

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
