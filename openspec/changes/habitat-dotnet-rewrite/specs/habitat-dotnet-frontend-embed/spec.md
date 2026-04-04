## ADDED Requirements

### Requirement: Frontend build is copied into the library's wwwroot before compilation
The build system SHALL copy the SolidJS production build output (`habitat/ui/dist/**`) into `sdks/dotnet/Absurd.Dashboard/wwwroot/` as part of the library build process, before `dotnet build` is invoked.

#### Scenario: Make target builds UI then copies artifacts
- **WHEN** the developer runs `make build` (or the CI equivalent)
- **THEN** the SolidJS build runs first, its output is copied to `wwwroot/`, and `dotnet build` succeeds with the latest frontend

#### Scenario: Missing frontend build fails library build explicitly
- **WHEN** `dotnet build` is run without first copying the frontend artifacts
- **THEN** the build fails with a clear error indicating that `wwwroot/` is empty or missing

### Requirement: Frontend assets are embedded as resources in the library assembly
All files under `sdks/dotnet/Absurd.Dashboard/wwwroot/` SHALL be declared as `EmbeddedResource` items in `Absurd.Dashboard.csproj` so they are included in the compiled assembly and the NuGet package.

#### Scenario: Embedded resources are accessible at runtime
- **WHEN** the library is loaded at runtime
- **THEN** `Assembly.GetManifestResourceNames()` returns entries for each file under `wwwroot/`

#### Scenario: NuGet package contains embedded frontend
- **WHEN** `dotnet pack` produces `Absurd.Dashboard.nupkg`
- **THEN** the package assembly contains manifest resources for all frontend files (no separate content files required)

### Requirement: wwwroot output directory is excluded from source control
The `sdks/dotnet/Absurd.Dashboard/wwwroot/` directory SHALL be listed in `.gitignore` so generated frontend artifacts are not committed.

#### Scenario: Generated files not tracked
- **WHEN** a developer runs the frontend build and inspects git status
- **THEN** the `wwwroot/` directory contents appear as untracked/ignored rather than modified or staged

### Requirement: Static asset paths in index.html are rewritten to the library's static prefix
Before embedding, the build step (or at serve time) SHALL ensure that asset references in `index.html` that point to `/_static/` are correctly rewritten to the effective static base path when served.

#### Scenario: Asset references resolve correctly under sub-path
- **WHEN** the dashboard is mounted at `/habitat` and a browser loads `index.html`
- **THEN** asset `<script>` and `<link>` tags reference `/habitat/_static/assets/...` rather than `/_static/assets/...`
