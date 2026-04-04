## Context

Habitat currently ships as a standalone Go binary. It connects to Postgres, queries the same `absurd.*` tables the worker uses, and serves a compiled SolidJS single-page app. The API surface is small: ~7 REST endpoints returning JSON, plus static file serving and an `index.html` injection step that embeds a runtime config object so the SPA knows its base path.

The .NET SDK (`Absurd.Sdk`) already uses Npgsql for direct Postgres access. The new library (`Absurd.Dashboard`) follows the same pattern and lives in the same solution, letting teams add observability to any ASP.NET application without a second deployment artifact.

## Goals / Non-Goals

**Goals:**
- Provide a NuGet package (`Absurd.Dashboard`) that embeds the Habitat dashboard into any ASP.NET 8+ application.
- Preserve the existing REST API contract (URL paths, JSON shapes) so the unchanged SolidJS frontend continues to work.
- Allow the dashboard to be mounted at any sub-path (e.g. `/habitat`, `/ops/absurd`).
- Keep the library self-contained: the compiled frontend assets travel inside the NuGet package as embedded resources.
- Match the existing DI registration style of `Absurd.Sdk` (`AddAbsurd(...)` / `MapAbsurdDashboard(...)`).

**Non-Goals:**
- Replacing or modifying the SolidJS frontend.
- Removing the Go habitat binary (it is retained as a reference implementation).
- Supporting non-Npgsql database drivers.
- Adding authentication or authorization to the dashboard (out of scope; consumers should apply their own middleware).

## Decisions

### 1. Project layout — new `Absurd.Dashboard` class library

A dedicated project `sdks/dotnet/Absurd.Dashboard/` is added to `Absurd.Sdk.sln`. It targets `net8.0` and uses the `Microsoft.NET.Sdk.Web` SDK so `EmbeddedResource` glob patterns and the `StaticWebAssets` infrastructure work without workarounds.

*Alternative considered: embed assets into `Absurd.Sdk` itself.* Rejected — it would add a heavy ASP.NET dependency to the core SDK that pure worker applications don't need.

### 2. Frontend asset embedding

The SolidJS build (`habitat/ui/dist/`) is copied into `sdks/dotnet/Absurd.Dashboard/wwwroot/` as part of the build. All files under `wwwroot/` are declared as `<EmbeddedResource>` in the csproj. At runtime the library reads these streams via `Assembly.GetManifestResourceStream`. A static `EmbeddedFileProvider` backed by the assembly is constructed once and reused for static file serving.

This mirrors how Razor Class Libraries embed static assets, but uses the simpler `EmbeddedResource` approach to avoid the full RCL toolchain.

*Alternative: copy assets at publish time* — rejected; the library would not be self-contained and consumers would need to manage extra files.

### 3. Registration API — `AddAbsurdDashboard` + `MapAbsurdDashboard`

```csharp
// Service registration
builder.Services.AddAbsurdDashboard(opts =>
{
    opts.ConnectionString = "Host=localhost;Database=mydb";
});

// Endpoint mapping (minimal API style)
app.MapAbsurdDashboard("/habitat");
```

`MapAbsurdDashboard(prefix)` uses `IEndpointRouteBuilder.Map` (the modern approach, compatible with ASP.NET 8 minimal APIs and controller apps alike). Internally it calls `app.Map(prefix, branch => { ... })` to create an isolated pipeline branch that strips the prefix before routing.

*Alternative: `IApplicationBuilder.UseAbsurdHabitat()`* — considered for simplicity but `MapAbsurdDashboard` integrates better with endpoint routing, middleware short-circuiting, and authorization filters if consumers want to protect the branch.

### 4. Sub-path and base-path injection

When serving `index.html`, the library injects a `<base href="...">` tag and a `window.__HABITAT_RUNTIME_CONFIG__` script block containing `{ basePath, apiBasePath, staticBasePath }` — identical to the Go implementation. The prefix is derived from the `pathPrefix` argument supplied to `MapAbsurdDashboard`, with `X-Forwarded-Prefix` / `X-Forwarded-Path` override support for reverse proxy scenarios.

Static assets are served from the prefix `/_static/` within the branch (i.e., `<mountPath>/_static/**`).

### 5. Database access — Npgsql directly, no connection pooling service

`HabitatOptions` accepts a connection string. The library creates an `NpgsqlDataSource` (which manages its own pool) and injects it into all query handlers. It does not share or reuse the `NpgsqlDataSource` from `Absurd.Sdk` — the two can operate independently. Consumers that want to share a connection pool can supply a pre-built `NpgsqlDataSource` via an overload.

*Rationale:* keeps `Absurd.Dashboard` usable without `Absurd.Sdk` installed.

### 6. Query fidelity — port Go queries verbatim

All SQL queries (tasks, queues, events, metrics) are ported directly from `habitat/internal/server/handlers.go`. Dynamic table identifiers (e.g. `absurd.t_<queue>`) are constructed with the same sanitization logic and passed as inline SQL (not as parameters) — matching the Go approach. Queue names are validated against a whitelist fetched from `absurd.queues` before being interpolated to prevent injection.

### 7. SPA fallback routing

Any path within the mounted branch that is not an API route and not a static asset path returns `index.html` (with config injected). This covers deep-link navigation in the SPA.

## Risks / Trade-offs

- **Queue name interpolation** → SQL injection risk if queue names are not validated. Mitigation: queue names are always fetched from `absurd.queues` first; only names returned by that query are interpolated into dynamic SQL.
- **Embedded asset size** → compiled SolidJS bundle adds ~300–500 KB to the NuGet package. Acceptable for a dev/ops tool; consumers that need zero overhead can strip the package reference.
- **`net8.0` minimum** → older LTS targets are not supported. Rationale: `IEndpointRouteBuilder.Map` with the required overloads was stabilized in .NET 8.
- **No auth out of the box** → the dashboard is open if mounted without a gate. Risk accepted; documented in README. Consumers should apply `RequireAuthorization` or an IP-filter middleware around the branch.
- **Frontend/backend version skew** → if the Go frontend is updated, the embedded copy in the .NET library must be rebuilt. Mitigation: CI gate verifies the frontend build artifact is current before packaging.

## Migration Plan

1. Build the SolidJS frontend (`npm run build` in `habitat/ui`).
2. Copy dist output to `sdks/dotnet/Absurd.Dashboard/wwwroot/`; add to `.gitignore` (generated artifact).
3. Add `Absurd.Dashboard.csproj` to the solution; implement endpoints and query logic.
4. Run `dotnet pack` to produce the NuGet package.
5. Update `Makefile` to automate the frontend copy step before `dotnet build`.
6. No rollback required — the Go binary and .NET library are independent artifacts.

## Open Questions

_(none — all questions resolved)_
