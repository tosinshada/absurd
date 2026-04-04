## Context

Absurd is a Postgres-native durable workflow system. Its TypeScript and Python SDKs communicate with the database exclusively through the stored procedures defined in `sql/absurd.sql`. The SDKs are intentionally thin: they call stored procedures via parameterised queries, marshal JSON, and handle the worker polling loop. There is no HTTP layer or broker — Postgres *is* the broker.

The .NET SDK must replicate this exact database interaction model using Npgsql (the canonical .NET Postgres driver), expose an ergonomic C# API, and integrate naturally with the .NET ecosystem (dependency injection, `ILogger`, `CancellationToken`, `async`/`await`).

## Goals / Non-Goals

**Goals:**
- Full API parity with the TypeScript SDK (spawn, steps, sleep, events, worker, hooks, queue management, retry, cancellation).
- Target `net10.0` (current .NET LTS); ship as a NuGet package `Absurd.Sdk`.
- Async-first API using `Task<T>` / `ValueTask<T>` throughout.
- `ILogger<T>` integration and null-safe defaults (no required DI container).
- `CancellationToken` support on all blocking/async operations.
- XML doc comments on all public APIs.
- Integration tests using xUnit + Testcontainers (.NET) that mirror the Python test suite.

**Non-Goals:**
- Source generator or Roslyn analyzer for task registration.
- Synchronous (blocking) API surface.
- Support for .NET Framework, .NET Standard, or Mono.
- ORM integration (EF Core, Dapper) — raw Npgsql only.
- SDK-level UI or monitoring tooling (that is Habitat's job).

## Decisions

### 1 — Driver: Npgsql (not ADO.NET wrappers)

**Decision**: Use `Npgsql` directly rather than `Microsoft.Data.PostgreSql` or third-party wrappers.

**Rationale**: Npgsql is the official, actively maintained Postgres driver for .NET; it is what the .NET ecosystem expects. It supports `NpgsqlDataSource` (connection pooling, .NET 6+ idiomatic API) and has first-class `Jsonb` support. The alternative (generic `System.Data.Common`) would hide Postgres-specific types needed for `jsonb` parameters.

**Alternative considered**: Dapper — adds a dependency with no meaningful benefit since the query set is small and the stored-procedure call pattern is uniform.

### 2 — Entry point: `Absurd` class + `AbsurdOptions`

**Decision**: Mirror the TypeScript `new Absurd(options)` API with a C# `Absurd` class constructed from `AbsurdOptions`.

```csharp
var app = new Absurd(new AbsurdOptions {
    ConnectionString = "Host=localhost;Database=mydb",
    QueueName = "default",
    DefaultMaxAttempts = 5,
});
```

`AbsurdOptions.ConnectionString` falls back to `ABSURD_DATABASE_URL`, then `PGDATABASE`, then `postgresql://localhost/absurd` — matching TypeScript behaviour exactly.

**Alternative considered**: Factory method / builder — unnecessary complexity for a small options surface.

### 3 — Task registration: generic delegate `Func<TParams, TaskContext, Task>`

**Decision**: Tasks are registered as typed async delegates. JSON deserialisation of `params` is handled inside the SDK.

```csharp
app.RegisterTask<SendEmailParams>("send-email", async (p, ctx) => {
    var html = await ctx.Step("render", async () => $"<h1>{p.Template}</h1>");
    await ctx.Step("send", async () => new { accepted = p.To, html });
});
```

Params are deserialised from the `jsonb` column via `System.Text.Json`. The return type of `step` functions must be JSON-serialisable.

**Alternative considered**: Interface-based tasks (`ITask<TParams>`) — adds boilerplate without improving type safety given the delegate approach already enforces the contract.

### 4 — Step checkpointing: same SP calls as TypeScript SDK

**Decision**: `ctx.Step()` calls `absurd_begin_checkpoint` and `absurd_complete_checkpoint` (or their equivalent stored procedure names in `sql/absurd.sql`). The serialisation contract is identical to the TypeScript SDK — `System.Text.Json` with `JsonSerializerOptions` configured to match.

**Alternative considered**: A separate .NET-specific checkpoint table — rejected to keep schema parity and avoid migration complexity.

### 5 — Worker model: long-poll loop using `PeriodicTimer` + `SemaphoreSlim`

**Decision**: `StartWorker()` returns an `AbsurdWorker` that runs a `PeriodicTimer`-based poll loop. Concurrency is controlled by a `SemaphoreSlim`. Graceful shutdown is triggered by `worker.StopAsync(CancellationToken)`.

**Alternative considered**: `System.Threading.Channels` pipeline — valid but over-engineered for the current concurrency model.

### 6 — Hooks: interface `IAbsurdHooks` + optional injection

**Decision**: Hooks are expressed as an optional `IAbsurdHooks` implementation passed via `AbsurdOptions.Hooks`. Two methods: `BeforeSpawnAsync` and `WrapTaskExecutionAsync` — direct .NET equivalents of the TypeScript hook callbacks.

### 6.9 — `BindToConnection`: `DbConnection` + `DbTransaction?`, caller owns lifecycle

**Decision**: `BindToConnection` accepts `DbConnection` and an optional `DbTransaction?`, using the ADO.NET base abstractions rather than Npgsql-specific types.

```csharp
var bound = app.BindToConnection(conn, tx);
```

**Rationale**: In Npgsql, every `NpgsqlCommand` must have `.Transaction` explicitly set when an active transaction exists — unlike node-postgres where transaction state is implicit on the connection. Using `DbConnection`/`DbTransaction` (from `System.Data`, no extra dependency) makes this work transparently for both raw Npgsql and EF Core callers without requiring casts:

```csharp
// EF Core — no casts needed
var conn = context.Database.GetDbConnection();
var tx   = context.Database.CurrentTransaction!.GetDbTransaction();
var bound = app.BindToConnection(conn, tx);

// Raw Npgsql — idiomatic await using handles lifecycle
await using var conn = await dataSource.OpenConnectionAsync();
await using var tx   = await conn.BeginTransactionAsync();
var bound = app.BindToConnection(conn, tx);
```

**Lifecycle — the SDK never owns or closes the passed connection.** The caller is always responsible for disposal:
- For raw Npgsql callers, `await using` is the idiomatic pattern — no `owned` parameter is needed (unlike the TypeScript SDK's `owned: boolean` escape hatch, which exists because JavaScript has no RAII).
- For EF Core callers, EF Core owns the connection.
- `DisposeAsync` on a bound `Absurd` instance is a no-op for the connection.

**Lazy-open behaviour**: If `conn.State != ConnectionState.Open` when the first command is issued, the SDK SHALL call `await conn.OpenAsync()` once but SHALL NOT close it afterward. This handles EF Core's lazy-open pattern without breaking raw Npgsql callers who open the connection themselves.

**Alternative considered**: `BindToConnection(NpgsqlConnection, NpgsqlTransaction?)` — works but requires callers to cast from `DbConnection`/`DbTransaction`, adding friction for EF Core integration.

**Alternative considered**: Adding an `owned` parameter — rejected; the C# `IAsyncDisposable`/`await using` pattern is the correct resource management idiom and an `owned` flag would be unexpected in .NET.

### 7 — Package layout

```
sdks/dotnet/
  Absurd.Sdk/           ← library (net10.0)
    Absurd.Sdk.csproj
    Absurd.cs
    AbsurdOptions.cs
    TaskContext.cs
    Worker.cs
    Hooks.cs
    Errors.cs
    Internal/
  Absurd.Sdk.Tests/     ← xUnit + Testcontainers
    Absurd.Sdk.Tests.csproj
  Absurd.Sdk.sln
```

### 8 — Serialisation: `System.Text.Json` with `JsonSerializerDefaults.Web`

**Decision**: Use `System.Text.Json` (inbox, no extra dependency) with `JsonSerializerDefaults.Web` (camelCase, case-insensitive) to match the JSON conventions used by the TypeScript SDK and stored in Postgres `jsonb` columns.

**Alternative considered**: `Newtonsoft.Json` — would add a dependency and is not the modern .NET idiom.

## Risks / Trade-offs

- **Npgsql version skew** → Pin to the latest stable Npgsql 9.x release; use `<PackageReference>` version range `[9,)` to allow minor updates. Re-test on each Absurd SQL schema migration.
- **System.Text.Json round-trip fidelity** → `JsonElement` / dynamic types used internally; strict `[JsonSerializable]` source generators are opt-in at the application level. Risk: subtle JSON differences between TypeScript and .NET serializers (e.g., `undefined` vs `null`). Mitigation: integration tests that round-trip payloads through the stored procedures.
- **Lease timeout detection** → TypeScript uses `fatalOnLeaseTimeout` to exit the process. .NET equivalent raises an unhandled exception on the worker's background thread which propagates through the host. Applications using `IHostedService` should register an `UnhandledException` handler.
- **.NET 10 preview stability** → .NET 10 was RTM in November 2025; it is stable. No known issues.

## Migration Plan

1. Add `sdks/dotnet/` to the repository (no changes to existing files except Makefile and docs).
2. Publish pre-release `Absurd.Sdk` to NuGet when implementation is complete.
3. No database migrations required.
4. Rollback: remove `sdks/dotnet/` — zero impact on existing SDK users.

## Decisions (resolved from open questions)

### 9 — `WorkBatchAsync` accepts `CancellationToken`

**Decision**: `WorkBatchAsync(workerId, claimTimeoutSeconds, batchSize, CancellationToken cancellationToken = default)` SHALL accept a `CancellationToken`. When the token fires, the method stops claiming new tasks and returns after in-flight tasks complete (or are abandoned if the token is already cancelled before any tasks start).

**Rationale**: Serverless runtimes (AWS Lambda, Azure Functions Consumption plan) impose hard wall-clock limits. A `CancellationToken` passed from the host's shutdown signal lets the function complete cleanly within the deadline rather than being forcibly killed mid-task.

### 10 — DI extension package is a follow-on

**Decision**: `Microsoft.Extensions.DependencyInjection` integration (`services.AddAbsurd(...)`) will be shipped as a separate `Absurd.Sdk.Extensions` NuGet package in a follow-on change, not inlined in `Absurd.Sdk`.

**Rationale**: Keeps the core library free of the `Microsoft.Extensions.*` dependency tree, which matters for consumers who don't use the generic host (e.g., console tools, Lambda handlers using the minimal bootstrap). The extension package can take a `Microsoft.Extensions.DependencyInjection.Abstractions` reference without pulling it into the core.
