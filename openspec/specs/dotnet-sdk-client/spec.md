## ADDED Requirements

### Requirement: Client construction from connection string
The `Absurd` class SHALL be constructable from an `AbsurdOptions` object whose `ConnectionString` property is set. When `ConnectionString` is omitted, the SDK SHALL fall back to the `ABSURD_DATABASE_URL` environment variable, then `PGDATABASE`, then the default `postgresql://localhost/absurd`.

#### Scenario: Explicit connection string
- **WHEN** `new Absurd(new AbsurdOptions { ConnectionString = "Host=localhost;Database=mydb" })` is called
- **THEN** the client connects to the specified database

#### Scenario: Environment variable fallback
- **WHEN** no `ConnectionString` is provided and `ABSURD_DATABASE_URL` is set
- **THEN** the client uses the value of `ABSURD_DATABASE_URL`

#### Scenario: Default connection string
- **WHEN** no `ConnectionString` is provided and no relevant environment variables are set
- **THEN** the client uses `postgresql://localhost/absurd`

### Requirement: Client construction from existing NpgsqlDataSource
The `Absurd` class SHALL accept an existing `NpgsqlDataSource` via `AbsurdOptions.DataSource`, allowing callers to supply a pre-configured connection pool.

#### Scenario: External pool reuse
- **WHEN** `AbsurdOptions.DataSource` is a pre-created `NpgsqlDataSource`
- **THEN** the client uses that data source and does NOT dispose it on `DisposeAsync`

### Requirement: Queue name configuration
`AbsurdOptions.QueueName` SHALL default to `"default"` and be used as the target queue for all operations when not overridden per-call.

#### Scenario: Default queue name
- **WHEN** `QueueName` is not set in `AbsurdOptions`
- **THEN** all operations target the `"default"` queue

### Requirement: Spawn a task
`app.SpawnAsync<TParams>(taskName, params, options?)` SHALL insert a new task into the queue and return a `SpawnResult` containing `TaskId`, `RunId`, `Attempt`, and `Created`.

#### Scenario: New task spawned
- **WHEN** `SpawnAsync` is called with a unique task name and parameters
- **THEN** a task row is created in Postgres and `Created` is `true`

#### Scenario: Idempotent spawn
- **WHEN** `SpawnAsync` is called twice with the same `IdempotencyKey`
- **THEN** the second call returns the existing task with `Created` set to `false`

### Requirement: Fetch task result snapshot
`app.FetchTaskResultAsync(taskId)` SHALL return the current `TaskSnapshot` for the given task, or `null` if no task with that ID exists.

#### Scenario: Existing task
- **WHEN** `FetchTaskResultAsync` is called with a known task ID
- **THEN** a `TaskSnapshot` is returned with the current status and result

#### Scenario: Unknown task
- **WHEN** `FetchTaskResultAsync` is called with an unknown task ID
- **THEN** `null` is returned

### Requirement: Poll task to terminal state
`app.AwaitTaskResultAsync(taskId, options?)` SHALL poll until the task reaches `completed`, `failed`, or `cancelled`, then return the final `TaskSnapshot`. It SHALL throw `TimeoutException` if `options.TimeoutSeconds` is exceeded.

#### Scenario: Task completes before timeout
- **WHEN** the task transitions to `completed` before the timeout
- **THEN** the final snapshot is returned

#### Scenario: Timeout exceeded
- **WHEN** the task has not reached a terminal state within `TimeoutSeconds`
- **THEN** `TimeoutException` is thrown

### Requirement: Emit event from outside a task
`app.EmitEventAsync(eventName, payload?, queueName?)` SHALL emit a named event on the specified queue (defaulting to the client queue).

#### Scenario: Event emitted on default queue
- **WHEN** `EmitEventAsync("order.shipped", payload)` is called without a queue override
- **THEN** the event is recorded on the client's default queue

#### Scenario: Event emitted on specified queue
- **WHEN** `EmitEventAsync("order.shipped", payload, "orders")` is called
- **THEN** the event is recorded on the `"orders"` queue

### Requirement: Cancel a task
`app.CancelTaskAsync(taskId, queueName?)` SHALL mark the task as cancelled.

#### Scenario: Task cancelled
- **WHEN** `CancelTaskAsync` is called with a valid task ID
- **THEN** the task transitions to `cancelled` state

### Requirement: Retry a failed task
`app.RetryTaskAsync(taskId, options?)` SHALL re-queue a failed task for another attempt. `options.MaxAttempts` increases the attempt limit; `options.SpawnNewTask` (default `false`) controls in-place vs new-task retry.

#### Scenario: In-place retry
- **WHEN** `RetryTaskAsync` is called on a failed task with `SpawnNewTask = false`
- **THEN** the task is re-queued on its existing record

#### Scenario: New-task retry
- **WHEN** `RetryTaskAsync` is called with `SpawnNewTask = true`
- **THEN** a new task is spawned and the original task is left as-is

### Requirement: Queue management
`app.CreateQueueAsync`, `app.DropQueueAsync`, and `app.ListQueuesAsync` SHALL create, drop, and list queues respectively.

#### Scenario: Create and list queues
- **WHEN** `CreateQueueAsync("emails")` is called
- **THEN** `ListQueuesAsync()` returns a list containing `"emails"`

#### Scenario: Drop queue
- **WHEN** `DropQueueAsync("emails")` is called
- **THEN** the queue and its tables are removed from the database

### Requirement: Bind to a connection
`app.BindToConnection(DbConnection conn, DbTransaction? tx = null)` SHALL return a new `Absurd` instance that routes all operations through the given connection and transaction. The SDK SHALL attach `tx` to every `NpgsqlCommand` it issues. The SDK SHALL NOT dispose or close `conn` at any point — the caller is solely responsible for the connection's lifecycle.

If `conn.State` is not `Open` when the first command is issued, the SDK SHALL call `await conn.OpenAsync()` once before issuing that command, but SHALL NOT close it afterward.

#### Scenario: Transactional spawn with raw Npgsql
- **WHEN** the caller creates a connection and transaction via `await using`, calls `BindToConnection(conn, tx)`, and invokes `SpawnAsync`
- **THEN** the spawn participates in that transaction and rolls back atomically if the transaction is not committed

#### Scenario: Transactional spawn with EF Core
- **WHEN** `conn` and `tx` are obtained from `context.Database.GetDbConnection()` and `context.Database.CurrentTransaction.GetDbTransaction()`
- **THEN** `SpawnAsync` on the bound instance participates in EF Core's active transaction with no casts required

#### Scenario: Bound dispose does not close connection
- **WHEN** `DisposeAsync` is called on a bound `Absurd` instance
- **THEN** `conn` is NOT closed or disposed

#### Scenario: Lazy connection open
- **WHEN** `BindToConnection` is called with a connection whose `State` is `Closed`
- **THEN** the SDK opens it before the first command and does not close it afterward

### Requirement: Dispose releases resources
`await app.DisposeAsync()` SHALL stop any running worker and close the connection pool if the client owns it.

#### Scenario: Dispose owned pool
- **WHEN** `Absurd` was constructed with a `ConnectionString` (pool is owned)
- **THEN** `DisposeAsync` disposes the `NpgsqlDataSource`

#### Scenario: Dispose external pool
- **WHEN** `Absurd` was constructed with an external `NpgsqlDataSource`
- **THEN** `DisposeAsync` does NOT dispose the external data source
