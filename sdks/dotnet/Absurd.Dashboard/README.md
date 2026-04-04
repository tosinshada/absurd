# Absurd.Dashboard

ASP.NET Core middleware that embeds the [Absurd](https://github.com/absurd-dev/absurd) Habitat monitoring dashboard into any web application.

## Installation

```shell
dotnet add package Absurd.Dashboard
```

The package is self-contained: the SolidJS frontend is compiled and embedded directly in the assembly, no separate static file hosting is required.

## Prerequisites

- .NET 8 or later
- A running PostgreSQL database with the [Absurd schema](../../sql/absurd.sql) applied

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

Then open `http://localhost:5000/habitat` in your browser.

## Configuration

### Via delegate

```csharp
builder.Services.AddAbsurdDashboard(opts =>
{
    opts.ConnectionString = "Host=localhost;Database=mydb";
});
```

### Via `IConfiguration` / appsettings.json

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

### Options reference

| Property           | Type     | Required | Description                                              |
|--------------------|----------|----------|----------------------------------------------------------|
| `ConnectionString` | `string` | Yes      | PostgreSQL connection string used to query Absurd tables |

A missing `ConnectionString` throws `InvalidOperationException` at startup.

## Mounting

```csharp
app.MapAbsurdDashboard(string pathPrefix = "/habitat")
```

- `pathPrefix` must start with `/`. The default is `/habitat`.
- All dashboard routes are isolated under this prefix; no existing routes are affected.
- The SPA, static assets, and API are all served from within the branch.

## Reverse Proxy Support

When the application runs behind a reverse proxy, the dashboard reads `X-Forwarded-Prefix`, `X-Forwarded-Path`, or `X-Script-Name` headers to construct correct absolute URLs for the SPA. No additional configuration is needed.

## API Endpoints

All endpoints are served under `<pathPrefix>/`:

| Endpoint                      | Description                                 |
|-------------------------------|---------------------------------------------|
| `/_healthz`                   | Database connectivity check (200/503)       |
| `/api/config`                 | Runtime configuration used by the SPA       |
| `/api/queues`                 | Per-queue task state summary                |
| `/api/queues/{name}/tasks`    | Tasks in a specific queue                   |
| `/api/queues/{name}/events`   | Events in a specific queue                  |
| `/api/metrics`                | Per-queue depth and timing metrics          |
| `/api/tasks`                  | List tasks with filtering and pagination    |
| `/api/tasks/{id}`             | Full task detail with run history           |
| `/api/tasks/retry`            | Retry a failed task (`POST`)                |
| `/api/events`                 | List events across queues                   |

## Building the Frontend

The NuGet package ships with pre-built frontend assets. If you are working from source, run:

```shell
make dashboard-ui
```

This builds the SolidJS UI in `habitat/ui` and copies the output into `sdks/dotnet/Absurd.Dashboard/wwwroot/`. The build will fail with a clear error message if `wwwroot/` is empty when you run `dotnet build`.

## License

MIT
