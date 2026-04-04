## Why

Absurd currently has SDKs for TypeScript and Python, leaving .NET developers without a native client. As .NET 10 is the current LTS release of the platform, adding a first-class C# SDK brings durable workflow capability to the large .NET ecosystem — cloud services, ASP.NET APIs, background services, and enterprise workloads — without requiring interop or shell-outs.

## What Changes

- Add a new `sdks/dotnet/` directory containing a .NET 10 C# class library (`Absurd.Sdk`) targeting `net10.0`.
- Publish the package to NuGet as `Absurd.Sdk`.
- Implement full feature parity with the TypeScript SDK: task registration, spawning, steps, sleep, event await, heartbeat, event emission, cancellation, retry, queue management, worker loop, and single-batch processing.
- Add lifecycle hooks (`BeforeSpawn`, `WrapTaskExecution`) matching the TypeScript hook model.
- Add `bindToConnection` equivalent via `BindToConnection(DbConnection, DbTransaction?)` for transactional usage with raw Npgsql and EF Core.
- Ship XML doc comments for all public APIs.
- Add unit/integration tests under `sdks/dotnet/tests/` using xUnit and testcontainers-dotnet.
- Update top-level `Makefile` with build/test targets for the .NET SDK.
- Add `docs/sdk-dotnet.md` documenting the SDK in the same style as `docs/sdk-typescript.md`.

## Capabilities

### New Capabilities

- `dotnet-sdk-client`: Core `Absurd` client class — construction, connection management, queue operations, spawn, fetchTaskResult, awaitTaskResult, emitEvent, cancelTask, retryTask.
- `dotnet-sdk-task-execution`: Task registration, `TaskContext` with step, beginStep/completeStep, sleepFor, sleepUntil, awaitEvent, awaitTaskResult, heartbeat, emitEvent.
- `dotnet-sdk-worker`: Worker loop (`StartWorker`), single-batch processing (`WorkBatch`), graceful shutdown.
- `dotnet-sdk-hooks`: `AbsurdHooks` — `BeforeSpawn` and `WrapTaskExecution` hooks for tracing/context propagation.

### Modified Capabilities

## Impact

- **New directory**: `sdks/dotnet/` (library project + test project)
- **New docs**: `docs/sdk-dotnet.md`
- **Makefile**: new targets `dotnet-build`, `dotnet-test`
- **Dependencies**: `Npgsql` (Postgres driver), `Microsoft.Extensions.Logging.Abstractions`, `xUnit` + `testcontainers` (test only)
- No changes to SQL schema, existing SDKs, or the Habitat UI
