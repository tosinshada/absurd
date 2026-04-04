## 1. Repository & Project Setup

- [x] 1.1 Create `sdks/dotnet/` directory with `Absurd.Sdk.sln`, `Absurd.Sdk/Absurd.Sdk.csproj` (targeting `net10.0`), and `Absurd.Sdk.Tests/Absurd.Sdk.Tests.csproj`
- [x] 1.2 Add `Npgsql` and `Microsoft.Extensions.Logging.Abstractions` NuGet references to `Absurd.Sdk.csproj`
- [x] 1.3 Add `xunit`, `xunit.runner.visualstudio`, `Testcontainers.PostgreSql`, and `Microsoft.NET.Test.Sdk` to `Absurd.Sdk.Tests.csproj`
- [x] 1.4 Add NuGet package metadata to `Absurd.Sdk.csproj` (`PackageId`, `Version`, `Description`, `Authors`, `RepositoryUrl`)

## 2. Core Types & Options

- [x] 2.1 Create `AbsurdOptions.cs` — `ConnectionString`, `DataSource` (NpgsqlDataSource), `QueueName` (default `"default"`), `DefaultMaxAttempts` (default `5`), `Log` (ILogger), `Hooks` (IAbsurdHooks?)
- [x] 2.2 Create `SpawnOptions.cs` — `MaxAttempts`, `RetryStrategy`, `Headers`, `Queue`, `Cancellation`, `IdempotencyKey`
- [x] 2.3 Create `SpawnResult.cs` — `TaskId`, `RunId`, `Attempt`, `Created`
- [x] 2.4 Create `TaskSnapshot.cs` — mirrors the TypeScript `TaskResult` shape (status, result, error, etc.)
- [x] 2.5 Create `RetryStrategy.cs` — discriminated union / sealed class hierarchy: `FixedRetryStrategy`, `ExponentialRetryStrategy`, `NoRetryStrategy`
- [x] 2.6 Create `CancellationPolicy.cs` — `MaxDuration` (seconds), `MaxDelay` (seconds)
- [x] 2.7 Create `Errors.cs` — `TimeoutException` (Absurd-specific), `TaskCancelledException`, `FailedTaskException`

## 3. Hooks Interface

- [x] 3.1 Create `IAbsurdHooks.cs` with default no-op implementations of `BeforeSpawnAsync` and `WrapTaskExecutionAsync`
- [x] 3.2 Verify partial implementation (only one method) compiles without error

## 4. TaskContext

- [x] 4.1 Create `TaskContext.cs` exposing `TaskId` (string), `Headers` (IReadOnlyDictionary<string, JsonElement>)
- [x] 4.2 Implement `StepAsync<T>(name, Func<Task<T>> fn)` — call `absurd_begin_checkpoint`, check if done (return cached), else execute fn, call `absurd_complete_checkpoint`
- [x] 4.3 Implement `BeginStepAsync<T>(name)` → `StepHandle<T>` and `CompleteStepAsync<T>(handle, value)`
- [x] 4.4 Implement `SleepForAsync(stepName, TimeSpan)` — translates to stored procedure call
- [x] 4.5 Implement `SleepUntilAsync(stepName, DateTimeOffset)` — translates to stored procedure call
- [x] 4.6 Implement `AwaitEventAsync(eventName, options?)` — suspend + timeout support; throw `TimeoutException` on expiry
- [x] 4.7 Implement `ctx.AwaitTaskResultAsync(taskId, options)` — cross-queue wait, checkpoint as step, validate queue differs
- [x] 4.8 Implement `HeartbeatAsync(seconds?)` — extend claim lease
- [x] 4.9 Implement `EmitEventAsync(eventName, payload?)` — idempotent first-emit semantics
- [x] 4.10 Implement internal cancellation detection at each checkpoint call

## 5. Task Registration

- [x] 5.1 Create `TaskRegistrationOptions.cs` — `Name`, `Queue`, `DefaultMaxAttempts`, `DefaultCancellation`
- [x] 5.2 Add `RegisterTask<TParams>(name, Func<TParams, TaskContext, Task> handler, TaskRegistrationOptions? options = null)` to `Absurd` class
- [x] 5.3 Store registrations in an internal `Dictionary<string, TaskRegistration>` keyed by task name
- [x] 5.4 Implement params deserialisation using `System.Text.Json` with `JsonSerializerDefaults.Web`

## 6. Core Client (`Absurd` class)

- [x] 6.1 Create `Absurd.cs` constructor — accept `AbsurdOptions`, resolve connection string from env, create `NpgsqlDataSource` if needed, track ownership
- [x] 6.2 Implement `SpawnAsync<TParams>(taskName, params, SpawnOptions?)` — call spawn stored procedure, apply `BeforeSpawn` hook, return `SpawnResult`
- [x] 6.3 Implement `FetchTaskResultAsync(taskId)` → `TaskSnapshot?`
- [x] 6.4 Implement `AwaitTaskResultAsync(taskId, options?)` — polling loop with configurable interval; throw `TimeoutException` on expiry
- [x] 6.5 Implement `EmitEventAsync(eventName, payload?, queueName?)` — queue-level event emission
- [x] 6.6 Implement `CancelTaskAsync(taskId, queueName?)`
- [x] 6.7 Implement `RetryTaskAsync(taskId, options?)` — in-place and new-task modes
- [x] 6.8 Implement `CreateQueueAsync(name)`, `DropQueueAsync(name)`, `ListQueuesAsync()`
- [x] 6.9 Implement `BindToConnection(DbConnection, DbTransaction?)` — return bound `Absurd` instance; attach `tx` to all commands; lazy-open connection if `State != Open`; never close or dispose the connection
- [x] 6.10 Implement `DisposeAsync` — stop worker, dispose owned `NpgsqlDataSource`

## 7. Worker

- [x] 7.1 Create `WorkerOptions.cs` — `Concurrency`, `ClaimTimeoutSeconds`, `BatchSize`, `PollIntervalSeconds`, `WorkerId`, `FatalOnLeaseTimeout`, `OnError`
- [x] 7.2 Create `AbsurdWorker.cs` — `PeriodicTimer` poll loop, `SemaphoreSlim` concurrency gate
- [x] 7.3 Implement task claim loop: call claim stored procedure, dispatch each claimed task to a `Task.Run` slot
- [x] 7.4 Implement handler dispatch: deserialise params, build `TaskContext`, apply `WrapTaskExecution` hook, invoke handler, write result or error
- [x] 7.5 Implement `StopAsync(CancellationToken)` — stop polling, drain in-flight tasks
- [x] 7.6 Implement `FatalOnLeaseTimeout` watchdog — background timer per task, `Environment.Exit` on 2× expiry
- [x] 7.7 Add `StartWorkerAsync(WorkerOptions?)` to `Absurd` class
- [x] 7.8 Implement `WorkBatchAsync(workerId, claimTimeoutSeconds, batchSize, CancellationToken cancellationToken = default)` — check token before claiming; claim once, run all, return; respect token cancellation mid-flight (stop claiming, let in-flight tasks finish)

## 8. Integration Tests

- [x] 8.1 Set up `TestContainerFixture` that spins up Postgres, applies `sql/absurd.sql`, and creates a test queue
- [x] 8.2 Test: spawn and await a simple task with one step
- [x] 8.3 Test: step caching — verify `fn` is not called on retry
- [x] 8.4 Test: idempotent spawn (same `IdempotencyKey` returns `Created = false`)
- [x] 8.5 Test: `SleepFor` — task suspends and resumes after elapsed time (use short duration)
- [x] 8.6 Test: `AwaitEvent` — task suspends, event emitted externally, task resumes with payload
- [x] 8.7 Test: `AwaitEvent` timeout — `TimeoutException` thrown after timeout
- [x] 8.8 Test: `CancelTask` — running task detects cancellation at next step
- [x] 8.9 Test: `RetryTask` — failed task re-queued successfully
- [x] 8.10 Test: `BeforeSpawn` hook — header injected into spawned task
- [x] 8.11 Test: `WrapTaskExecution` hook — wrapper called and handler executes
- [x] 8.12 Test: `BindToConnection` — spawn inside transaction rolls back with transaction
- [x] 8.13 Test: worker concurrency — `Concurrency = 2` runs two tasks in parallel
- [x] 8.14 Test: `WorkBatch` — claims and runs tasks, returns when done

## 9. Documentation

- [x] 9.1 Create `docs/sdk-dotnet.md` — full API reference matching the structure of `docs/sdk-typescript.md`
- [x] 9.2 Add link to `docs/sdk-dotnet.md` from `docs/index.md` and `README.md`
- [x] 9.3 Add code examples in `sdks/dotnet/Absurd.Sdk/examples/` covering common patterns (spawn + await, worker, hooks, sleep, events)
