## ADDED Requirements

### Requirement: BeforeSpawn hook
When `AbsurdOptions.Hooks` implements `IAbsurdHooks.BeforeSpawnAsync`, the SDK SHALL call it before every `SpawnAsync` invocation. The hook SHALL receive the task name, parameters, and spawn options, and SHALL return (possibly modified) `SpawnOptions`.

#### Scenario: Hook injects headers
- **WHEN** `BeforeSpawnAsync` returns `SpawnOptions` with additional headers
- **THEN** those headers are stored on the spawned task

#### Scenario: Hook is not set
- **WHEN** `AbsurdOptions.Hooks` is `null`
- **THEN** `SpawnAsync` proceeds without any hook invocation

### Requirement: WrapTaskExecution hook
When `AbsurdOptions.Hooks` implements `IAbsurdHooks.WrapTaskExecutionAsync`, the SDK SHALL call it wrapping every task handler execution. The hook SHALL receive a `TaskContext` and an `execute` delegate and SHALL call `execute()` to run the handler.

#### Scenario: Wrapper executes handler
- **WHEN** `WrapTaskExecutionAsync` is provided and a task runs
- **THEN** the hook is called with the context and delegate; the handler runs when `execute()` is called

#### Scenario: Wrapper propagates trace context
- **WHEN** the hook reads `ctx.Headers["traceId"]` and sets it on an ambient context before calling `execute()`
- **THEN** all code inside the handler observes the trace context

#### Scenario: Hook must call execute
- **WHEN** `WrapTaskExecutionAsync` does NOT call `execute()`
- **THEN** the task handler is skipped and the task fails with an appropriate error

### Requirement: IAbsurdHooks interface
The `IAbsurdHooks` interface SHALL declare:
- `Task<SpawnOptions> BeforeSpawnAsync(string taskName, object? parameters, SpawnOptions options)`
- `Task WrapTaskExecutionAsync(TaskContext ctx, Func<Task> execute)`

Both methods SHALL be optional (provided as default interface method no-ops) to allow partial implementation.

#### Scenario: Partial implementation compiles
- **WHEN** a class implements only `BeforeSpawnAsync`
- **THEN** the class compiles without errors and `WrapTaskExecutionAsync` is a no-op
