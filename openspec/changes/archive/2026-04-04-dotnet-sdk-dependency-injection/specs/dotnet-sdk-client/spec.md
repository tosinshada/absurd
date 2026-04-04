## ADDED Requirements

### Requirement: IAbsurdClient interface
The SDK SHALL expose an `IAbsurdClient` interface that covers the data-plane operations of `AbsurdClient`, enabling DI-friendly substitution in unit tests.

The interface SHALL include:
- `SpawnAsync<TParams>(string taskName, TParams parameters, SpawnOptions? options = null, CancellationToken ct = default)`
- `FetchTaskResultAsync(Guid taskId, string? queueName = null, CancellationToken ct = default)`
- `AwaitTaskResultAsync(Guid taskId, AwaitOptions? options = null, CancellationToken ct = default)`
- `EmitEventAsync(string eventName, object? payload = null, string? queueName = null, CancellationToken ct = default)`
- `CancelTaskAsync(Guid taskId, string? queueName = null, CancellationToken ct = default)`
- `RetryTaskAsync(Guid taskId, RetryTaskOptions? options = null, CancellationToken ct = default)`
- `BindToConnection(DbConnection connection, DbTransaction? transaction = null)`
- `RegisterTask<TParams>(string name, Func<TParams, TaskContext, Task> handler, TaskRegistrationOptions? options = null)`
- `StartWorkerAsync(WorkerOptions? options = null)`

#### Scenario: AbsurdClient implements IAbsurdClient
- **WHEN** `AbsurdClient` is compiled
- **THEN** it implements `IAbsurdClient` without any change to its public method signatures

#### Scenario: Unit test substitution via interface
- **WHEN** a unit test creates a mock implementing `IAbsurdClient`
- **AND** injects it into the system under test
- **THEN** the system under test can call `SpawnAsync`, `RegisterTask`, or `StartWorkerAsync` without requiring a real Postgres connection
