## 1. Project Setup

- [x] 1.1 Create `sdks/dotnet/Absurd.Dashboard/Absurd.Dashboard.csproj` targeting `net8.0` with `Microsoft.NET.Sdk.Web`, `Npgsql`, and `Microsoft.AspNetCore.StaticFiles` dependencies
- [x] 1.2 Add `Absurd.Dashboard` project to `Absurd.Sdk.sln`
- [x] 1.3 Add `sdks/dotnet/Absurd.Dashboard/wwwroot/` to `.gitignore`
- [x] 1.4 Configure `<EmbeddedResource Include="wwwroot/**" />` in the csproj so all frontend files are embedded in the assembly
- [x] 1.5 Add a build guard that fails with a clear message if `wwwroot/` is empty at build time

## 2. Frontend Build Pipeline

- [x] 2.1 Add a `Makefile` target that runs the SolidJS build (`npm run build` in `habitat/ui`) and copies `habitat/ui/dist/**` to `sdks/dotnet/Absurd.Dashboard/wwwroot/`
- [x] 2.2 Update the root `Makefile` (or CI pipeline) to run the frontend copy step before `dotnet build`
- [x] 2.3 Verify the embedded resources are accessible via `Assembly.GetManifestResourceNames()` in a manual smoke test

## 3. Options and DI Registration

- [x] 3.1 Create `HabitatOptions` class with `ConnectionString` (required) and `BasePath` (optional) properties
- [x] 3.2 Implement `AddAbsurdDashboard(Action<HabitatOptions>)` extension on `IServiceCollection` that registers `NpgsqlDataSource` and all required services
- [x] 3.3 Implement `AddAbsurdDashboard(IConfiguration)` overload that binds options from a configuration section
- [x] 3.4 Add fail-fast validation: throw `InvalidOperationException` at startup if `ConnectionString` is missing

## 4. Embedded Static File Serving

- [x] 4.1 Implement `EmbeddedFileProvider`-backed static file serving for `/_static/**` within the mounted branch, loading assets from assembly manifest resources
- [x] 4.2 Implement correct `Content-Type` mapping for `.js`, `.css`, `.woff2`, `.png`, `.svg`, `.json` extensions
- [x] 4.3 Implement `index.html` serving with `<base href>` injection and `window.__HABITAT_RUNTIME_CONFIG__` script injection, mirroring the Go `renderIndexHTML` logic
- [x] 4.4 Implement `X-Forwarded-Prefix` / `X-Forwarded-Path` / `X-Script-Name` header extraction for reverse-proxy base-path override (port from Go `extractForwardedPrefix`)
- [x] 4.5 Implement SPA fallback: any non-API, non-static path within the branch returns the injected `index.html`

## 5. MapAbsurdDashboard Routing

- [ ] 5.1 Implement `MapAbsurdDashboard(string pathPrefix)` extension on `IEndpointRouteBuilder` that creates an isolated pipeline branch stripping the prefix
- [ ] 5.2 Wire internal routes within the branch: `/_healthz`, `/_static/**`, `/api/**`, and SPA fallback (`/**`)
- [ ] 5.3 Verify that routes outside the prefix are unaffected by the branch

## 6. Health Check Endpoint (`/_healthz`)

- [ ] 6.1 Implement `GET /_healthz` handler: ping the database via `NpgsqlDataSource` and return 200 `ok` or 503 `database unavailable`

## 7. Config Endpoint (`/api/config`)

- [ ] 7.1 Implement `GET /api/config` handler returning `{ basePath, apiBasePath, staticBasePath }` JSON

## 8. Queues API (`/api/queues`, `/api/queues/{name}`)

- [ ] 8.1 Port `handleQueues` from Go: query `absurd.queues`, return array of `{ queueName, createdAt }`
- [ ] 8.2 Port `handleQueueResource` from Go: query detailed metrics for the named queue; return 404 if queue not found
- [ ] 8.3 Implement queue name validation: only interpolate names returned from `absurd.queues` into dynamic SQL

## 9. Metrics API (`/api/metrics`)

- [ ] 9.1 Port `handleMetrics` from Go: iterate all queues, build per-table metric queries using validated queue names, return `{ queues: [...] }`

## 10. Tasks API (`/api/tasks`, `/api/tasks/{id}`, `/api/tasks/retry`)

- [ ] 10.1 Port `handleTasks` from Go: implement all query parameters (`q`, `status`, `queue`, `taskName`, `taskId`, `after`, `before`, `page`, `perPage`), cap `perPage` at 200
- [ ] 10.2 Port task candidate query and merge/sort logic across queues
- [ ] 10.3 Port full-text search filter (`matchesTaskFilters`) in Go
- [ ] 10.4 Port `listRecentTaskNames` with 1-minute in-memory cache
- [ ] 10.5 Port `handleTaskDetail` from Go: return full task JSON including run history and checkpoints; 404 for missing/invalid IDs
- [ ] 10.6 Port `handleRetryTask` from Go: accept `{ taskId, queueName }`, validate state, call retry stored procedure; return 400 for non-retryable state

## 11. Events API (`/api/events`)

- [ ] 11.1 Port `handleEvents` from Go: support `queue`, `taskId`, `after`, `before`, `page`, `perPage` parameters; query event tables; return paginated response

## 12. Error Handling and JSON Consistency

- [ ] 12.1 Implement a shared `WriteJson` helper that sets `Content-Type: application/json` and serializes the response
- [ ] 12.2 Implement a shared error response helper that returns `{ "error": "<message>" }` JSON for 4xx/5xx responses
- [ ] 12.3 Add timeout handling for long-running queries (30 s default, 120 s for full-text search queries), consistent with Go implementation

## 13. Tests

- [ ] 13.1 Add unit tests for base-path injection logic (`renderIndexHTML` equivalent) covering empty prefix, custom prefix, and reverse-proxy prefix
- [ ] 13.2 Add unit tests for queue name validation / SQL identifier building
- [ ] 13.3 Add integration tests (using `testcontainers-dotnet` or similar) for at least: `/api/metrics`, `/api/tasks`, `/api/tasks/{id}`, `/api/queues`, `/_healthz`
- [ ] 13.4 Add a test verifying the embedded resource manifest contains the expected frontend files after the build pipeline runs

## 14. Documentation

- [ ] 14.1 Add `README.md` to `sdks/dotnet/Absurd.Dashboard/` covering installation, configuration options, and a minimal usage example for ASP.NET 8
- [ ] 14.2 Update the root `docs/habitat.md` (or create `docs/habitat-dotnet.md`) to document the .NET package alongside the Go binary
