## ADDED Requirements

### Requirement: Register a typed task handler
`app.RegisterTask<TParams>(name, handler)` SHALL associate a task name with an async delegate `Func<TParams, TaskContext, Task>`. Params are deserialised from the `jsonb` column using `System.Text.Json`.

#### Scenario: Handler invoked with correct params
- **WHEN** a task named `"send-email"` is registered with `TParams = SendEmailParams`
- **THEN** when the worker claims the task, the handler is called with a deserialised `SendEmailParams` instance

#### Scenario: Registration options
- **WHEN** `RegisterTask` is called with `TaskRegistrationOptions` specifying `Queue`, `DefaultMaxAttempts`, or `DefaultCancellation`
- **THEN** those values override the client defaults for that task

### Requirement: Step execution with caching
`ctx.StepAsync<T>(name, fn)` SHALL execute `fn` and persist the return value as a checkpoint. On subsequent runs of the same task, the cached value SHALL be returned without calling `fn` again.

#### Scenario: First execution runs fn
- **WHEN** a step named `"fetch"` is executed for the first time
- **THEN** `fn` is called and its return value is stored in Postgres

#### Scenario: Cached execution skips fn
- **WHEN** the task is retried and `ctx.StepAsync("fetch", fn)` is called again
- **THEN** `fn` is NOT called and the previously stored value is returned

#### Scenario: Step return value is JSON-serialisable
- **WHEN** `fn` returns a value that cannot be serialised to JSON
- **THEN** `JsonException` is thrown before the checkpoint is written

### Requirement: Advanced step control with BeginStep/CompleteStep
`ctx.BeginStepAsync<T>(name)` SHALL return a `StepHandle<T>`. If `handle.IsDone` is `true`, the cached value is available as `handle.State`. Otherwise, the caller SHALL call `ctx.CompleteStepAsync(handle, value)` to write the checkpoint.

#### Scenario: Resume from cached checkpoint
- **WHEN** `BeginStepAsync` is called for an already-completed step
- **THEN** `handle.IsDone` is `true` and `handle.State` contains the cached value

#### Scenario: Complete a new checkpoint
- **WHEN** `BeginStepAsync` is called for a new step and `CompleteStepAsync` is called with a value
- **THEN** the value is persisted and the task continues

### Requirement: Sleep for a duration
`ctx.SleepForAsync(stepName, TimeSpan duration)` SHALL suspend the task until the duration has elapsed.

#### Scenario: Task resumes after duration
- **WHEN** `SleepForAsync("cooldown", TimeSpan.FromHours(1))` is called
- **THEN** the task is suspended and resumes no earlier than 1 hour later

### Requirement: Sleep until an absolute time
`ctx.SleepUntilAsync(stepName, DateTimeOffset deadline)` SHALL suspend the task until the given UTC timestamp.

#### Scenario: Task resumes at deadline
- **WHEN** `SleepUntilAsync("deadline", future)` is called
- **THEN** the task resumes no earlier than `future`

### Requirement: Await a named event
`ctx.AwaitEventAsync(eventName, options?)` SHALL suspend the task until the named event is emitted on the task's queue. It SHALL return the event payload. If `options.TimeoutSeconds` is set and elapses, a `TimeoutException` SHALL be thrown.

#### Scenario: Event received
- **WHEN** `AwaitEventAsync("order.shipped")` is called and the event is later emitted
- **THEN** the task resumes with the event payload

#### Scenario: Timeout before event
- **WHEN** `AwaitEventAsync` is called with a timeout and no event arrives within that time
- **THEN** `TimeoutException` is thrown

### Requirement: Await another task's result (from within a task)
`ctx.AwaitTaskResultAsync(taskId, options)` SHALL durably wait for a task in a different queue to reach a terminal state. The wait SHALL be checkpointed as a step.

#### Scenario: Cross-queue task result
- **WHEN** `ctx.AwaitTaskResultAsync(childId, new { Queue = "workers" })` is called
- **THEN** the current task suspends until the child task completes, and the child's final snapshot is returned

#### Scenario: Different queue required
- **WHEN** `options.Queue` is the same as the current task's queue
- **THEN** an `InvalidOperationException` is thrown

### Requirement: Heartbeat to extend lease
`ctx.HeartbeatAsync(seconds?)` SHALL extend the current run's claim by the given number of seconds (default: the worker's `ClaimTimeout`).

#### Scenario: Lease extended
- **WHEN** `HeartbeatAsync(300)` is called
- **THEN** the run's expiry is moved forward by 300 seconds

### Requirement: Emit event from within a task
`ctx.EmitEventAsync(eventName, payload?)` SHALL emit a named event on the task's queue. Only the first emission per event name takes effect (idempotent).

#### Scenario: Event emitted once
- **WHEN** `ctx.EmitEventAsync("order.completed", data)` is called twice with the same name
- **THEN** only the first emission is stored

### Requirement: Task context exposes task ID and headers
`ctx.TaskId` SHALL expose the current task's ID. `ctx.Headers` SHALL expose the read-only JSON headers attached to the task.

#### Scenario: Access task metadata
- **WHEN** a task handler reads `ctx.TaskId` and `ctx.Headers`
- **THEN** the values match the spawned task's ID and headers

### Requirement: Cancellation detection
When a task is cancelled, the next call to `ctx.StepAsync`, `ctx.HeartbeatAsync`, or `ctx.AwaitEventAsync` SHALL throw `TaskCancelledException` (internal), causing the task framework to mark the run as cancelled.

#### Scenario: Cancellation detected at next step
- **WHEN** `CancelTaskAsync` is called on a running task and the handler reaches a `StepAsync` call
- **THEN** the task terminates with `cancelled` status
