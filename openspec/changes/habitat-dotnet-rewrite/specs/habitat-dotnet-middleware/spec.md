## ADDED Requirements

### Requirement: Library is registered via service collection
The library SHALL provide an `AddAbsurdDashboard` extension method on `IServiceCollection` that accepts a configuration delegate or `IConfiguration` section to supply connection options.

#### Scenario: Registration with delegate
- **WHEN** an application calls `services.AddAbsurdDashboard(opts => opts.ConnectionString = "...")`
- **THEN** the required services are registered in the DI container without error

#### Scenario: Registration with configuration section
- **WHEN** an application calls `services.AddAbsurdDashboard(config.GetSection("Habitat"))`
- **THEN** options are bound from the configuration section and services are registered

#### Scenario: Missing connection string
- **WHEN** `AddAbsurdDashboard` is called without a connection string
- **THEN** an `InvalidOperationException` is thrown at startup (fail-fast)

### Requirement: Dashboard is mounted at a configurable sub-path
The library SHALL provide a `MapAbsurdDashboard(string pathPrefix)` extension method on `IEndpointRouteBuilder` that mounts all Habitat routes under the supplied prefix.

#### Scenario: Mount at custom path
- **WHEN** an application calls `app.MapAbsurdDashboard("/habitat")`
- **THEN** all Habitat routes are accessible under `/habitat/**` and return 404 for requests outside that prefix

#### Scenario: Mount at root
- **WHEN** an application calls `app.MapAbsurdDashboard("/")`
- **THEN** Habitat routes are served from the root path

#### Scenario: Path collision does not affect host routes
- **WHEN** the host application has routes outside the Habitat prefix
- **THEN** those routes are unaffected by the Habitat mount

### Requirement: Base-path is injected into index.html at serve time
The library SHALL inject a `<base href>` tag and a `window.__HABITAT_RUNTIME_CONFIG__` script block into `index.html` before sending the response, reflecting the effective mount path.

#### Scenario: Runtime config reflects mount prefix
- **WHEN** a browser requests the SPA root (e.g. `GET /habitat/`)
- **THEN** the served HTML contains `<base href="/habitat/">` and `window.__HABITAT_RUNTIME_CONFIG__` with `basePath`, `apiBasePath`, and `staticBasePath` set relative to `/habitat`

#### Scenario: Reverse proxy prefix override
- **WHEN** a request carries an `X-Forwarded-Prefix: /proxy` header and the mount path is `/habitat`
- **THEN** the injected base path is `/proxy/habitat/` and the runtime config reflects the full proxy-aware path

### Requirement: SPA deep-link navigation is supported
The library SHALL return the (config-injected) `index.html` for any request path within the mounted branch that is not an API route or static asset path.

#### Scenario: Deep link request
- **WHEN** a browser requests `GET /habitat/tasks/some-task-id`
- **THEN** the server returns the `index.html` response (HTTP 200, `Content-Type: text/html`)

### Requirement: Static assets are served from embedded resources
The library SHALL serve compiled SolidJS static assets (JS, CSS, fonts) from an `/_static/` sub-path within the mounted branch using files embedded in the assembly.

#### Scenario: Static asset request
- **WHEN** a browser requests `GET /habitat/_static/assets/index-<hash>.js`
- **THEN** the server returns the embedded file with appropriate `Content-Type` and HTTP 200

#### Scenario: Missing static asset returns 404
- **WHEN** a browser requests a static asset path that does not correspond to an embedded file
- **THEN** the server returns HTTP 404

### Requirement: Health check endpoint is available
The library SHALL expose a `/_healthz` endpoint that returns HTTP 200 when the database is reachable and HTTP 503 otherwise.

#### Scenario: Database reachable
- **WHEN** a client sends `GET /_healthz` and the database connection succeeds
- **THEN** the response is HTTP 200 with body `ok`

#### Scenario: Database unreachable
- **WHEN** a client sends `GET /_healthz` and the database ping fails
- **THEN** the response is HTTP 503
