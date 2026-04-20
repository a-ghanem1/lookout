# Lookout.AspNetCore

Zero-config dev-time diagnostics dashboard for ASP.NET Core. Captures HTTP requests, EF Core queries (with N+1 detection), outbound HTTP, cache hits/misses, exceptions, logs, Hangfire jobs, and `Lookout.Dump()` output — correlated per-request in a browser dashboard.

## Tech stack

- **Backend:** ASP.NET Core, target `net8.0` + `net10.0`
- **Dashboard:** React + Vite, embedded as static assets (not Blazor)
- **Storage:** SQLite with FTS5, 24h time-based retention + 50k entry cap
- **Instrumentation:**
  - `DbCommandInterceptor` for EF Core
  - `DiagnosticListener` for raw ADO.NET / Dapper
  - `DelegatingHandler` for outbound `HttpClient`
  - `Activity.Current?.RootId` for request correlation
- **Safety:** dev-only by default; throws in production unless explicit override

Plan: `.claude/LOOKOUT_PLAN.md` (13-week plan, 4 phases). Current phase: Phase 0 — Validation.

## Working agreements

### 1. Follow best practices

- Idiomatic, modern C# (nullable enabled, `async`/`await` end-to-end, `ConfigureAwait` where appropriate in library code, `IAsyncDisposable` for resources).
- Follow ASP.NET Core conventions: DI-first, options pattern for config, `IHostedService` for background work, middleware composition over custom pipelines.
- Keep the package zero-config for consumers — sane defaults, opt-in extensibility.
- No breaking public API changes without a deliberate decision; treat `Lookout.AspNetCore` surface as a contract.
- React side: TypeScript, functional components, hooks; no class components; keep the dashboard bundle small.
- Do not add features, abstractions, or config knobs beyond what the current task requires.

### 2. Suggest a conventional commit message after every change

After each meaningful change, output a **single-line** [Conventional Commits](https://www.conventionalcommits.org/) message the user can copy. Format:

```
<type>(<scope>): <subject>
```

- Types: `feat`, `fix`, `refactor`, `perf`, `test`, `docs`, `build`, `ci`, `chore`
- Scope examples: `core`, `ef`, `http`, `dashboard`, `storage`, `config`
- Imperative mood, lowercase subject, no trailing period, ≤72 chars total.

Do not run `git commit` — just suggest the message.

### 3. Test coverage for everything

Every change ships with tests. No exceptions for "trivial" changes.

- **Unit tests:** xUnit + FluentAssertions + NSubstitute. Test public behavior, not implementation details.
- **Integration tests:** `WebApplicationFactory<T>` for middleware, interceptors, and end-to-end capture paths. Use a real SQLite file (or `:memory:` where appropriate) — do **not** mock the storage layer in integration tests.
- **Dashboard tests:** Vitest + React Testing Library for components; Playwright for the dashboard end-to-end smoke path.
- New public API → new tests. Bug fix → regression test that fails before the fix. Refactor → existing tests must stay green; add tests if coverage gaps are exposed.
- Run the full test suite before declaring a task complete.

## Repository layout (expected)

```
src/
  Lookout.AspNetCore/        # main library
  Lookout.Dashboard/         # React + Vite app, built into wwwroot/
tests/
  Lookout.AspNetCore.Tests/          # unit
  Lookout.AspNetCore.IntegrationTests/ # WebApplicationFactory-based
  Lookout.Dashboard.Tests/            # Vitest
```

## Out of scope for v1

Do not suggest: Blazor dashboard, cloud-hosted mode, auth/multi-tenant, or any feature explicitly deferred to v1.1+ in `.claude/LOOKOUT_PLAN.md`.
