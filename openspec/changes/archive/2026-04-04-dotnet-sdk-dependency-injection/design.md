## Context

The `AbsurdClient` is currently constructed manually by callers. In .NET hosted applications (ASP.NET Core, Worker Service), services are registered into an `IServiceCollection` and resolved through `IServiceProvider`. There is no first-class path to integrate `AbsurdClient` with this system, meaning developers must manually bridge the options model, logging, and disposal — work the DI container is designed to handle.

.NET's `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Options` packages are lightweight, ship as part of the .NET 10 BCL, and are already transitively present via `Microsoft.Extensions.Logging.Abstractions`.

## Goals / Non-Goals

**Goals:**
- Provide `IServiceCollection.AddAbsurd(Action<AbsurdOptions>)` extension method for fluent configuration
- Support binding `AbsurdOptions` from `IConfiguration` (e.g., `appsettings.json`) via the standard `IOptions<T>` pattern
- Automatically wire the DI-provided `ILoggerFactory` into the client's logger
- Extract `IAbsurdClient` to allow DI-friendly mocking in unit tests
- Register `AbsurdClient` as a singleton (one pool per application lifetime)
- Handle `DisposeAsync` cleanly via `IHostApplicationLifetime` or `IAsyncDisposable` registration

**Non-Goals:**
- Publishing a separate NuGet package (`Absurd.Sdk.DependencyInjection`) — DI support ships in `Absurd.Sdk` itself
- Scoped or transient lifetimes — `AbsurdClient` owns a connection pool and must be singleton
- Auto-registering task handlers — task registration remains an explicit code-time operation
- ASP.NET Core health checks or middleware — out of scope for this change

## Decisions

### 1. Same package, not a separate NuGet package

**Options considered:**
- **A. Separate `Absurd.Sdk.DependencyInjection` package** — follows Entity Framework / SignalR convention but adds friction for users who just want DI
- **B. Embed DI extensions in `Absurd.Sdk`** — all dependencies (`Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Options`) are already transitively present in .NET 10

**Decision: Option B.** The additional package weight is negligible, and removing that friction improves the default experience without forcing a second package reference.

### 2. Extract IAbsurdClient interface

`AbsurdClient` implements a concrete class today. Extracting `IAbsurdClient` (covering `SpawnAsync`, `FetchTaskResultAsync`, and `BindToConnection`) allows tests to substitute a fake or mock without starting a Postgres connection.

**Alternatives considered:**
- **Wrapper/decorator class** — more boilerplate, still requires the concrete class at runtime
- **No interface** — acceptable for simple apps but blocks unit testing patterns; DI-first consumers expect an interface

**Decision: Introduce `IAbsurdClient`.** `AbsurdClient` implements `IAbsurdClient`. Both registered in the DI container as the same singleton (`services.AddSingleton<IAbsurdClient, AbsurdClient>(factory)`).

### 3. IOptions\<AbsurdOptions\> binding vs direct Action\<AbsurdOptions\>

Standard .NET libraries (e.g., HttpClient, DbContext) expose both a delegate form and an `IConfiguration` bind:

```csharp
// Delegate form — most common
services.AddAbsurd(opts => opts.ConnectionString = "...");

// IConfiguration section form — appsettings.json binding
services.AddAbsurd(configuration.GetSection("Absurd"));
```

Internally the delegate form calls `services.Configure<AbsurdOptions>(configure)`, and the factory reads `IOptions<AbsurdOptions>` from the container. This avoids a second options snapshot and integrates naturally with reload-on-change (though `AbsurdClient` is singleton and won't re-initialize on reload — that's documented).

**Decision: Support both overloads.** Backing store is always `IOptions<AbsurdOptions>`.

### 4. Logging: ILoggerFactory in the factory delegate

`AbsurdOptions.Log` is an `ILogger?`. In DI scenarios, the caller should not have to resolve `ILoggerFactory` manually. The `AddAbsurd` factory delegate resolves `ILoggerFactory` from the provider and sets `options.Log = loggerFactory.CreateLogger<AbsurdClient>()` before constructing the client — unless the caller already set `Log` explicitly (caller wins).

### 5. Disposal via IServiceProvider

`AbsurdClient` implements `IAsyncDisposable`. .NET's DI container calls `DisposeAsync` automatically for singleton services registered with a factory when the root `IServiceProvider` is disposed. No extra plumbing needed.

## Risks / Trade-offs

- **IAbsurdClient scope creep** — the interface may become a source of churn if the client's public API changes. Mitigation: include the full client surface (spawn, fetch, bind, register task, start worker) so that DI consumers have a single point of substitution without leaking the concrete class.
- **Options snapshot + singleton mismatch** — `IOptions<AbsurdOptions>` is computed once at first resolution; changes to `IConfiguration` after startup won't affect the running client. This is expected behavior for a singleton connection pool but should be documented clearly.
- **DataSource in options is not IConfiguration-bindable** — `NpgsqlDataSource` is a runtime object and cannot come from JSON config. Callers who need to supply an external data source must use the delegate overload. Mitigation: documented with a clear example.

## Migration Plan

1. Add `IAbsurdClient` interface — additive, no breaking changes
2. Add `ServiceCollectionExtensions.cs` with `AddAbsurd` overloads
3. Add package references to `.csproj`
4. Update XML doc and README with usage examples
5. No database schema changes; no migration needed
6. Existing callers constructing `AbsurdClient` directly are unaffected

## Open Questions

- ~~Should `IAbsurdClient` include `RegisterTask` and `StartWorkerAsync`?~~ **Resolved: Yes.** Both are included so that the full client surface is mockable and DI consumers never need to reference the concrete `AbsurdClient` type directly.
