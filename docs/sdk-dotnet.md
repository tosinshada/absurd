# .NET SDK

The .NET SDK (`Absurd.Sdk`) provides a full-featured async client for building
durable workflows with Absurd.  It uses [Npgsql](https://www.npgsql.org/) for
Postgres access and targets `net10.0`.

## Installation

```bash
dotnet add package Absurd.Sdk
```

Then add a using directive:

```csharp
using Absurd;
```

Before using the SDK, initialize the Absurd schema in Postgres and create at
least one queue.  See **[Database Setup and Migrations](./database.md)** and
**[absurdctl](./absurdctl.md)** for details.

## Creating a Client

```csharp
// Minimal — uses ABSURD_DATABASE_URL, then PGDATABASE,
// then postgresql://localhost/absurd; queue defaults to "default"
var app = new AbsurdClient();

// From a connection string
var app = new AbsurdClient(new AbsurdOptions
{
    ConnectionString = "Host=localhost;Database=mydb",
    QueueName = "default",
});

// From an existing NpgsqlDataSource (shared pool)
var dataSource = NpgsqlDataSource.Create("Host=localhost;Database=mydb");
var app = new AbsurdClient(new AbsurdOptions { DataSource = dataSource });

// Dispose when done
await app.DisposeAsync();
// or
await using var app = new AbsurdClient();
```

### `AbsurdOptions`

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `ConnectionString` | `string?` | `ABSURD_DATABASE_URL` → `PGDATABASE` → `postgresql://localhost/absurd` | Postgres connection string |
| `DataSource` | `NpgsqlDataSource?` | — | Pre-built connection pool; SDK does **not** dispose it |
| `QueueName` | `string` | `"default"` | Default queue for all operations |
| `DefaultMaxAttempts` | `int` | `5` | Default retry limit for spawned tasks |
| `Log` | `ILogger?` | `NullLogger` | Logger for SDK diagnostics |
| `Hooks` | `IAbsurdHooks?` | — | Lifecycle hooks for tracing / context propagation |

## Registering Tasks

```csharp
app.RegisterTask<SendEmailParams>("send-email", async (p, ctx) =>
{
    var rendered = await ctx.StepAsync("render", async () =>
        $"<h1>{p.Template}</h1>");

    await ctx.StepAsync("send", async () =>
        new { accepted = p.To, html = rendered });
});
```

### `TaskRegistrationOptions`

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `Queue` | `string?` | Client queue | Queue this task belongs to |
| `DefaultMaxAttempts` | `int?` | Client default | Default max attempts |
| `DefaultCancellation` | `CancellationPolicy?` | — | Default cancellation policy |

## Spawning Tasks

```csharp
var result = await app.SpawnAsync("send-email",
    new SendEmailParams { To = "user@example.com", Template = "welcome" },
    new SpawnOptions
    {
        MaxAttempts    = 10,
        RetryStrategy  = new ExponentialRetryStrategy { BaseSeconds = 2, Factor = 2, MaxSeconds = 300 },
        Headers        = new Dictionary<string, JsonElement> { ["traceId"] = JsonSerializer.SerializeToElement("abc") },
        IdempotencyKey = "welcome:user-42",
    });

Console.WriteLine(result.TaskId);   // "01234567-..."
Console.WriteLine(result.Created);  // true on first call, false on duplicate key
```

### `SpawnOptions`

| Option | Type | Description |
|--------|------|-------------|
| `MaxAttempts` | `int?` | Max retry attempts |
| `RetryStrategy` | `RetryStrategy?` | Backoff configuration |
| `Headers` | `Dictionary<string, JsonElement>?` | Metadata attached to the task |
| `Queue` | `string?` | Target queue |
| `Cancellation` | `CancellationPolicy?` | Auto-cancellation policy |
| `IdempotencyKey` | `string?` | Dedup key — existing task returned if key matches |

### `SpawnResult`

| Field | Type | Description |
|-------|------|-------------|
| `TaskId` | `string` | Unique task identifier (UUIDv7) |
| `RunId` | `string` | Current run identifier |
| `Attempt` | `int` | Attempt number |
| `Created` | `bool` | `false` if an existing task was returned (idempotency) |

## Task Results

### `app.FetchTaskResultAsync(taskId)`

Returns the current snapshot of a task, or `null` if the task does not exist.

```csharp
var snapshot = await app.FetchTaskResultAsync(taskId);
Console.WriteLine(snapshot?.State); // "pending", "running", "completed", ...
```

### `app.AwaitTaskResultAsync(taskId, timeoutSeconds?, pollIntervalSeconds?)`

Polls until the task reaches a terminal state (`completed`, `failed`,
`cancelled`).  Throws `AbsurdTimeoutException` if the timeout elapses.

```csharp
var final = await app.AwaitTaskResultAsync(taskId, timeoutSeconds: 30);
```

### `TaskSnapshot`

| Property | Type | Description |
|----------|------|-------------|
| `State` | `string` | `"pending"`, `"running"`, `"sleeping"`, `"completed"`, `"failed"`, `"cancelled"` |
| `Result` | `JsonElement?` | Return value (only when `State == "completed"`) |
| `Failure` | `JsonElement?` | Failure reason (only when `State == "failed"`) |
| `IsTerminal` | `bool` | `true` when state is `completed`, `failed`, or `cancelled` |

## Task Context (`TaskContext`)

The object passed to every task handler.

### `ctx.TaskId`

The unique identifier of the current task.

### `ctx.Headers`

`IReadOnlyDictionary<string, JsonElement>` of headers attached to the task.

### `ctx.StepAsync<T>(name, fn)`

Run an idempotent step.  The return value is cached in Postgres — on retries
the cached value is returned without calling `fn` again.

```csharp
var result = await ctx.StepAsync("fetch-data", async () =>
    new { ok = true, source = "api" });
```

The return value **must be JSON-serializable**.

### `ctx.BeginStepAsync<T>(name)` + `ctx.CompleteStepAsync<T>(handle, value)`

Advanced form of `StepAsync` for splitting step handling across two calls.

```csharp
var handle = await ctx.BeginStepAsync<MyState>("agent-turn");
if (handle.IsDone)
    return handle.State!;

var state = new MyState { Messages = ["hello"] };
await ctx.CompleteStepAsync(handle, state);
```

`handle.CheckpointName` contains the concrete key used in Postgres (e.g.
`"step-name"` or `"step-name#2"` for repeated names).

### `ctx.SleepForAsync(stepName, duration)`

Suspend the task for a duration.

```csharp
await ctx.SleepForAsync("cooldown", TimeSpan.FromHours(1));
```

### `ctx.SleepUntilAsync(stepName, wakeAt)`

Suspend the task until an absolute UTC time.

```csharp
await ctx.SleepUntilAsync("deadline", new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero));
```

### `ctx.AwaitEventAsync(eventName, stepName?, timeoutSeconds?)`

Suspend until a named event is emitted.  Returns the event payload as
`JsonElement`.  Throws `AbsurdTimeoutException` if the timeout expires.

```csharp
var payload = await ctx.AwaitEventAsync(
    "order.shipped",
    stepName: "wait-for-shipment",  // optional custom checkpoint name
    timeoutSeconds: 86400);
```

### `ctx.AwaitTaskResultAsync(taskId, queue, stepName?, timeoutSeconds?)`

Durably wait for another task's terminal result, checkpointed as a step.
`queue` **must differ** from the current task's queue.

```csharp
var child = await app.SpawnAsync("child-task", new { }, new SpawnOptions { Queue = "child-workers" });
var childResult = await ctx.AwaitTaskResultAsync(
    child.TaskId, "child-workers", timeoutSeconds: 60);
```

### `ctx.HeartbeatAsync(seconds?)`

Extend the current run's lease.  Defaults to the original claim timeout.

```csharp
await ctx.HeartbeatAsync(300); // extend by 5 minutes
```

### `ctx.EmitEventAsync(eventName, payload?)`

Emit an event on the current queue.  First emit per name wins.

```csharp
await ctx.EmitEventAsync("order.completed", new { OrderId = "42" });
```

## Events

Emit events from outside a task handler:

```csharp
await app.EmitEventAsync("order.shipped", new { TrackingNumber = "XYZ" });

// Emit to a specific queue
await app.EmitEventAsync("order.shipped", new { TrackingNumber = "XYZ" }, queueName: "orders");
```

## Cancellation

```csharp
await app.CancelTaskAsync(taskId);
await app.CancelTaskAsync(taskId, queueName: "other-queue");
```

Running tasks detect cancellation at the next `StepAsync`, `HeartbeatAsync`,
or `AwaitEventAsync` call.

## Retrying Failed Tasks

```csharp
var result = await app.RetryTaskAsync(taskId, new RetryTaskOptions
{
    MaxAttempts  = 5,       // increase the attempt limit
    SpawnNewTask = false,   // retry in-place (default) or spawn a new task
});
```

### `RetryTaskOptions`

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `MaxAttempts` | `int?` | `current + 1` | New maximum attempt limit |
| `SpawnNewTask` | `bool` | `false` | Spawn a brand-new task instead of retrying in-place |

## Queue Management

```csharp
await app.CreateQueueAsync("emails");
await app.DropQueueAsync("emails");
IReadOnlyList<string> queues = await app.ListQueuesAsync(); // ["default", "emails"]
```

## Starting a Worker

```csharp
var worker = await app.StartWorkerAsync(new WorkerOptions
{
    Concurrency         = 4,     // parallel tasks (default: 1)
    ClaimTimeoutSeconds = 120,   // lease duration in seconds (default: 120)
    BatchSize           = 4,     // tasks to claim per poll (default: Concurrency)
    PollIntervalSeconds = 0.25,  // seconds between idle polls (default: 0.25)
    WorkerId            = "web-1",                  // identifier for tracking (default: hostname:pid)
    FatalOnLeaseTimeout = true,  // exit process if task exceeds 2× lease (default: true)
    OnError             = ex => { Console.Error.WriteLine(ex); return Task.CompletedTask; },
});

// Graceful shutdown
await worker.StopAsync();
```

### `WorkerOptions`

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `Concurrency` | `int` | `1` | Parallel task executions |
| `ClaimTimeoutSeconds` | `int` | `120` | Lease duration per task |
| `BatchSize` | `int?` | `Concurrency` | Tasks claimed per poll |
| `PollIntervalSeconds` | `double` | `0.25` | Seconds between idle polls |
| `WorkerId` | `string?` | `"hostname:pid"` | Identifier for tracking |
| `FatalOnLeaseTimeout` | `bool` | `true` | Exit process when task exceeds 2× lease |
| `OnError` | `Func<Exception, Task>?` | — | Called on unhandled task errors |

### Single-Batch Processing

For cron-style or serverless workloads:

```csharp
await app.WorkBatchAsync(
    workerId: "lambda-1",
    claimTimeoutSeconds: 120,
    batchSize: 10);
```

## Binding to a Connection

Use `BindToConnection` to run Absurd operations on a specific database connection
(e.g., inside a transaction):

```csharp
await using var con = await dataSource.OpenConnectionAsync();
await using var tx  = await con.BeginTransactionAsync();

var bound = app.BindToConnection(con, tx);
await bound.SpawnAsync("my-task", new { Key = "value" });

await tx.CommitAsync();
```

The caller always owns the connection lifecycle — `BindToConnection` never closes
or disposes it.  Compatible with EF Core:

```csharp
var con    = context.Database.GetDbConnection();
var efTx   = context.Database.CurrentTransaction!.GetDbTransaction();
var bound  = app.BindToConnection(con, efTx);
```

## Hooks

### `BeforeSpawnAsync`

Called before every `SpawnAsync`.  Modify options to inject headers:

```csharp
var app = new AbsurdClient(new AbsurdOptions
{
    Hooks = new MyHooks(),
});

public class MyHooks : IAbsurdHooks
{
    public Task<SpawnOptions> BeforeSpawnAsync(
        string taskName, JsonElement? parameters, SpawnOptions options)
    {
        var traceId = Activity.Current?.TraceId.ToString();
        var headers = options.Headers is not null
            ? new Dictionary<string, JsonElement>(options.Headers)
            : new Dictionary<string, JsonElement>();
        if (traceId is not null)
            headers["traceId"] = JsonSerializer.SerializeToElement(traceId);

        return Task.FromResult(options with { Headers = headers });
    }
}
```

### `WrapTaskExecutionAsync`

Wraps task handler execution.  You **must** call and await `execute`:

```csharp
public async Task WrapTaskExecutionAsync(TaskContext ctx, Func<Task> execute)
{
    var traceId = ctx.Headers.TryGetValue("traceId", out var v) ? v.GetString() : null;
    using var activity = MyTracer.StartActivity("absurd.task", traceId);
    await execute();
}
```

## Error Types

| Type | Description |
|------|-------------|
| `AbsurdTimeoutException` | Thrown by `AwaitEventAsync` / `AwaitTaskResultAsync` when the timeout expires |
| `TaskCancelledException` | Internal — task was cancelled (never visible to handler code) |
| `FailedTaskException` | Internal — run already failed (never visible to handler code) |
| `SuspendTaskException` | Internal — task suspended for sleep/event (never visible to handler code) |

## Retry Strategies

| Class | Description |
|-------|-------------|
| `FixedRetryStrategy` | Wait `BaseSeconds` between each retry |
| `ExponentialRetryStrategy` | Wait `BaseSeconds * Factor^attempt`, capped at `MaxSeconds` |
| `NoRetryStrategy` | No automatic retries |

```csharp
new ExponentialRetryStrategy { BaseSeconds = 1, Factor = 2, MaxSeconds = 300 }
```

## Cancellation Policies

| Property | Type | Description |
|----------|------|-------------|
| `MaxDuration` | `double?` | Cancel task after N seconds of total lifetime |
| `MaxDelay` | `double?` | Cancel task if no checkpoint written for N seconds |

## Closing

```csharp
await app.DisposeAsync(); // stops worker, disposes owned NpgsqlDataSource
```
