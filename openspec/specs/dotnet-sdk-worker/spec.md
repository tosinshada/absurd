## ADDED Requirements

### Requirement: Start a long-lived worker
`app.StartWorkerAsync(options?)` SHALL start a background polling loop that claims and executes tasks. It SHALL return an `AbsurdWorker` handle.

#### Scenario: Worker claims and runs tasks
- **WHEN** `StartWorkerAsync` is called and a task is queued
- **THEN** the worker claims the task and invokes the registered handler

#### Scenario: Concurrency limit respected
- **WHEN** `WorkerOptions.Concurrency` is set to `n`
- **THEN** at most `n` tasks run concurrently at any time

### Requirement: Worker options
`WorkerOptions` SHALL support the following parameters, all optional with defaults:
- `Concurrency` (default: `1`) — number of parallel task executions
- `ClaimTimeoutSeconds` (default: `120`) — lease duration per task
- `BatchSize` (default: equals `Concurrency`) — tasks claimed per poll
- `PollIntervalSeconds` (default: `0.25`) — seconds between idle polls
- `WorkerId` (default: `hostname:pid`) — identifier for tracking
- `FatalOnLeaseTimeout` (default: `true`) — terminate process if a task exceeds 2× lease

#### Scenario: Default options
- **WHEN** `StartWorkerAsync()` is called with no options
- **THEN** the worker uses all default values defined above

#### Scenario: Custom concurrency
- **WHEN** `WorkerOptions.Concurrency = 4` is set
- **THEN** the worker runs up to 4 tasks simultaneously

### Requirement: Graceful shutdown
`worker.StopAsync(CancellationToken)` SHALL stop accepting new tasks, wait for in-flight tasks to complete, and release resources.

#### Scenario: Graceful stop
- **WHEN** `StopAsync` is called
- **THEN** no new tasks are claimed, and the method returns once all in-flight tasks have finished or the cancellation token fires

### Requirement: Fatal lease timeout behaviour
When `FatalOnLeaseTimeout` is `true` and a task has held a claim for more than `2 × ClaimTimeoutSeconds`, the worker SHALL terminate the process (exit code non-zero) to avoid zombie tasks.

#### Scenario: Process exits on lease timeout
- **WHEN** a task runs longer than twice the claim timeout and `FatalOnLeaseTimeout = true`
- **THEN** the process exits with a non-zero exit code

### Requirement: Error callback
`WorkerOptions.OnError` SHALL be an optional `Func<Exception, Task>` callback invoked when a task handler throws an unhandled exception.

#### Scenario: OnError invoked on unhandled exception
- **WHEN** a task handler throws and `WorkerOptions.OnError` is set
- **THEN** the callback is invoked with the exception before the task is marked as failed

### Requirement: Single-batch processing
`app.WorkBatchAsync(workerId, claimTimeoutSeconds, batchSize, CancellationToken cancellationToken = default)` SHALL claim and execute up to `batchSize` tasks, wait for all claimed tasks to finish, and return. This is suitable for cron-style or serverless invocations.

#### Scenario: Batch completes
- **WHEN** `WorkBatchAsync` is called and there are tasks queued
- **THEN** up to `batchSize` tasks are claimed and executed before the method returns

#### Scenario: Empty queue
- **WHEN** `WorkBatchAsync` is called and no tasks are queued
- **THEN** the method returns immediately without error

#### Scenario: CancellationToken fires before any tasks claimed
- **WHEN** the `CancellationToken` is already cancelled when `WorkBatchAsync` is called
- **THEN** no tasks are claimed and the method returns immediately

#### Scenario: CancellationToken fires during execution
- **WHEN** the `CancellationToken` fires while tasks are in flight
- **THEN** no additional tasks are claimed and the method returns after current in-flight tasks complete
