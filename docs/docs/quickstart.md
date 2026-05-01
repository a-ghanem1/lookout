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

For Hangfire job capture (optional):

```bash
dotnet add package Lookout.Hangfire
```

## 2. Wire up

Add three lines to `Program.cs`:

```csharp
// 1. Register services — call BEFORE AddDbContext
builder.Services.AddLookout();

// 2. Add middleware — call BEFORE UseRouting
app.UseLookout();

// 3. Mount the dashboard at /lookout
app.MapLookout();
```

:::tip Order matters
`AddLookout()` must come **before** `AddDbContext()` so the EF interceptor registers in the right position.

`UseLookout()` should come **before** `UseRouting()` and before any endpoint middleware so it captures the full request lifecycle.
:::

## 3. Run

```bash
dotnet run
```

Open the Lookout dashboard in your browser:

```
https://localhost:{port}/lookout
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

```
N+1 detected — 12 identical queries. Stack trace: OrderService.cs:47
```

The banner groups all identical SQL shapes, shows a count, and links the stack frame where the repeated query originated.

## Next steps

- [Configuration](./configuration) — body capture, retention window, redaction
- [Extensibility](./extensibility) — record custom events with `ILookoutRecorder` or `Lookout.Dump()`
- [Security model](./security) — understand the dev-only default and how to extend it
