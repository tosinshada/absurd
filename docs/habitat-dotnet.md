# Habitat for .NET

`Absurd.Dashboard` is an ASP.NET Core middleware package that embeds the Habitat
monitoring dashboard directly into your .NET web application — no separate
binary or sidecar required.

## Installation

```shell
dotnet add package Absurd.Dashboard
```

## Quick Start

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAbsurdDashboard(opts =>
{
    opts.ConnectionString = "Host=localhost;Database=mydb;Username=postgres;Password=secret";
});

var app = builder.Build();

app.MapAbsurdDashboard("/habitat"); // mount at any path you choose

app.Run();
```

Open `http://localhost:5000/habitat` in your browser to see the dashboard.

## Configuration

### Via delegate

```csharp
builder.Services.AddAbsurdDashboard(opts =>
{
    opts.ConnectionString = "Host=localhost;Database=mydb";
});
```

### Via appsettings.json

```json
{
  "AbsurdDashboard": {
    "ConnectionString": "Host=localhost;Database=mydb;Username=postgres;Password=secret"
  }
}
```

```csharp
builder.Services.AddAbsurdDashboard(
    builder.Configuration.GetSection("AbsurdDashboard"));
```

### Options

| Option             | Required | Description                                    |
|--------------------|----------|------------------------------------------------|
| `ConnectionString` | Yes      | PostgreSQL connection string for Absurd tables |

A missing `ConnectionString` throws `InvalidOperationException` at startup.

## Mounting

```csharp
app.MapAbsurdDashboard(pathPrefix: "/habitat");
```

- `pathPrefix` must start with `/`. Defaults to `/habitat`.
- All routes are isolated under this prefix; no existing application routes are affected.

## Reverse-Proxy Support

When running behind a reverse proxy the dashboard automatically uses the
`X-Forwarded-Prefix`, `X-Forwarded-Path`, or `X-Script-Name` request headers
to construct correct base URLs for the SPA — matching the behaviour of the
Go binary.

## Comparison with the Go Binary

| | Go binary (`habitat`) | .NET middleware |
|---|---|---|
| Distribution | Standalone binary downloaded from GitHub Releases | NuGet package |
| Hosting | Separate process on `:7890` | Embedded in the ASP.NET Core pipeline |
| Configuration | Flags / `HABITAT_*` env vars | `AddAbsurdDashboard(…)` / appsettings.json |
| Frontend assets | Embedded in the binary | Embedded in the assembly (built from same source) |
| Reverse-proxy support | `X-Forwarded-Prefix` / `-base-path` flag | `X-Forwarded-Prefix` / `pathPrefix` argument |

The two implementations expose identical REST APIs and the same SolidJS frontend.

## See Also

- [Go binary docs](habitat.md) — standalone `habitat` binary
- [SDK README](../sdks/dotnet/Absurd.Dashboard/README.md) — full API reference
