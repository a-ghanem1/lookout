---
sidebar_position: 6
title: Comparison
description: How Lookout compares to MiniProfiler.NET and Aspire Dashboard — what each tool is best for.
---

# Comparison

Lookout is not trying to replace MiniProfiler.NET or Aspire Dashboard. Each tool occupies a different part of the observability stack. Here is an honest comparison.

---

## Feature matrix

| | Lookout | MiniProfiler.NET | Aspire Dashboard |
|---|:---:|:---:|:---:|
| Zero-config install | Yes | Yes | No — requires AppHost |
| Standalone browser dashboard | Yes | In-page widget | Yes |
| N+1 detection | Yes | No | No |
| Exception + log capture | Yes | No | Partial |
| Cache capture | Yes | No | No |
| Background job capture (Hangfire) | Yes | No | No |
| Full-text search across entries | Yes | No | No |
| Keyboard navigation | Yes | No | No |
| Raw ADO.NET / Dapper capture | Yes | Yes | No |
| Production observability | No — dev-only | No | Yes |
| OpenTelemetry integration | No | No | Yes |
| In-page overlay (no separate tab) | No | Yes | No |
| Last major release | 2026 | 2022 | 2025 (actively developed) |

---

## MiniProfiler.NET

MiniProfiler renders a timing widget directly in the page — a small overlay in the corner of each page showing step durations and SQL counts. It is great for quickly spotting slow queries on a page-by-page basis in a running application.

**Where MiniProfiler wins:**
- In-page widget requires no separate browser tab — you see SQL counts on every page as you browse
- Works in production (the widget is gated by role membership)
- Extremely lightweight — minimal overhead, mature and battle-tested since 2011
- Excellent MVC / Razor Pages integration with step timing

**Where Lookout differs:**
- Lookout captures the full picture per request — not just SQL, but outbound HTTP, cache operations, logs, exceptions, and background jobs, all correlated
- N+1 detection with grouping and stack traces — MiniProfiler shows query counts but does not flag repeated shapes
- Standalone dashboard with search, keyboard navigation, and full-text across all captures
- Lookout does not add anything to the rendered page (no overlay)

**In practice:** teams that already rely on MiniProfiler's in-page widget for production SQL monitoring can add Lookout alongside it for deeper dev-time analysis without removing MiniProfiler.

---

## Aspire Dashboard

Aspire Dashboard is a production observability tool built on OpenTelemetry. It provides distributed traces, metrics, and logs for the entire service mesh in an Aspire application. It is the right tool when you need to understand how a request flows across multiple microservices.

**Where Aspire Dashboard wins:**
- Production-grade — built for distributed systems, not just local dev
- Full OpenTelemetry compatibility — collects traces and metrics from any OTEL-instrumented service
- Metrics and dashboards — not just traces
- Works across service boundaries — see the whole call graph

**Where Lookout differs:**
- Zero-config — add one package, three lines, done. No AppHost project, no OTEL exporter, no Aspire setup
- Works with any ASP.NET Core app — not tied to the Aspire hosting model
- Per-request detail: SQL text with parameters, N+1 detection, cache hit ratios, `Lookout.Dump()` — Aspire Dashboard shows OTEL spans, not raw SQL
- Dev-only by design — opinionated about staying safe

**In practice:** Lookout and Aspire Dashboard answer different questions. Aspire answers "why is my distributed system slow in staging?" Lookout answers "why did this single request issue 47 SQL queries?"

---

## When to use what

| Situation | Recommended tool |
|---|---|
| Daily development — finding N+1 bugs, SQL count, slow requests | **Lookout** |
| Debugging a production page that's loading slowly | **MiniProfiler** |
| Understanding distributed request flow across microservices | **Aspire Dashboard** |
| Production metrics and alerting | **Aspire Dashboard** (or Grafana/Prometheus) |
| Quick SQL count on every page while browsing | **MiniProfiler** |
| Full capture: SQL + HTTP + cache + logs + exceptions + jobs | **Lookout** |
