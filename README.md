# Lookout.AspNetCore

Zero-config dev-time diagnostics dashboard for ASP.NET Core. Captures HTTP requests, EF Core queries (with N+1 detection), outbound HTTP, cache hits/misses, exceptions, logs, Hangfire jobs, and `Lookout.Dump()` output — correlated per-request in a browser dashboard.

[![CI](https://github.com/a-ghanem1/Lookout/actions/workflows/ci.yml/badge.svg)](https://github.com/a-ghanem1/Lookout/actions/workflows/ci.yml)

## Quick start

```csharp
// Program.cs
builder.Services.AddLookout();

app.UseLookout();
app.MapLookout(); // serves dashboard at /lookout
```

Lookout is dev-only by default. It will throw at startup in Production unless explicitly overridden via `LookoutOptions`.

## Status

Pre-release — Week 1 scaffold. Not ready for consumption.
