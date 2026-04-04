## Why

Habitat currently exists only as a standalone Go binary, which creates a deployment gap for teams already running .NET applications — they must deploy and manage a separate process just to get observability into their Absurd queues. Rewriting Habitat as a .NET library lets any ASP.NET application embed the dashboard directly, eliminating the extra binary and making monitoring a first-class part of the application stack.

## What Changes

- **Remove Go habitat** as the primary distribution artifact; the Go source is retained for reference but the Go binary is no longer the canonical release target.
- **New .NET library** (`Absurd.Dashboard`) — an ASP.NET middleware library that exposes the full Habitat dashboard and REST API on a configurable sub-path of any ASP.NET application.
- **Frontend unchanged** — the existing SolidJS UI is kept as-is. Its production build is embedded into the library as static assets (served from `wwwroot`-style embedded resources).
- **Direct database access** — the .NET library connects to Postgres directly (via Npgsql) and replicates all query logic from the Go `handlers.go`, with no intermediate service.
- **ASP.NET integration** — the library is configured via `IServiceCollection` extension methods and wired into the middleware pipeline with `IApplicationBuilder`, following the same patterns as the existing Absurd.Sdk DI integration.
- **NuGet distribution** — the library is packaged as a NuGet package (`Absurd.Dashboard`) alongside the existing `Absurd.Sdk`.

## Capabilities

### New Capabilities

- `habitat-dotnet-middleware`: ASP.NET middleware that mounts the Habitat dashboard (UI + API) under a configurable sub-path, handles base-path injection into `index.html`, and serves embedded static assets.
- `habitat-dotnet-api`: REST API endpoints (`/api/metrics`, `/api/tasks`, `/api/tasks/{id}`, `/api/tasks/retry`, `/api/queues`, `/api/queues/{name}`, `/api/events`, `/api/config`) backed by direct Postgres queries, preserving the same JSON contract as the Go implementation.
- `habitat-dotnet-frontend-embed`: Build pipeline that compiles the SolidJS UI and embeds the output into the .NET library as embedded resources, making the library self-contained.

### Modified Capabilities

_(none — no existing specs change their requirements)_

## Impact

- **New project**: `sdks/dotnet/Absurd.Dashboard/` — new class library project added to the existing `Absurd.Sdk.sln`.
- **UI build**: `habitat/ui/` build output must be copied into `Absurd.Dashboard/wwwroot/` as part of the library build; `Makefile` updated accordingly.
- **Existing habitat Go code**: unmodified; retained as a reference implementation. API contract (JSON shapes, URL paths) must be preserved exactly.
- **No breaking changes** to `Absurd.Sdk` or any existing consumer.
- **Dependencies added**: `Npgsql` (Postgres driver), `Microsoft.AspNetCore.StaticFiles` (static file middleware).
