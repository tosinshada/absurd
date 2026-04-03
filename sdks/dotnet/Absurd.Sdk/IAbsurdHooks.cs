using System.Text.Json;

namespace Absurd;

/// <summary>
/// Lifecycle hooks for customising Absurd SDK behaviour.
/// Implement only the methods you need — both have default no-op implementations.
/// </summary>
public interface IAbsurdHooks
{
    /// <summary>
    /// Called before every <see cref="AbsurdClient.SpawnAsync{TParams}"/> invocation.
    /// Return a (possibly modified) <see cref="SpawnOptions"/> to inject headers or
    /// override spawn settings.
    /// </summary>
    /// <param name="taskName">Name of the task being spawned.</param>
    /// <param name="parameters">Raw task parameters (JSON-serialised).</param>
    /// <param name="options">Current spawn options.</param>
    /// <returns>The (potentially modified) spawn options to use.</returns>
    Task<SpawnOptions> BeforeSpawnAsync(string taskName, JsonElement? parameters, SpawnOptions options)
        => Task.FromResult(options);

    /// <summary>
    /// Wraps every task handler execution. You MUST call and await <paramref name="execute"/>
    /// to run the handler. Use this to restore ambient context (e.g. a trace ID) before the
    /// handler runs.
    /// </summary>
    /// <param name="ctx">The task context for the running task.</param>
    /// <param name="execute">Delegate that runs the task handler. Must be called.</param>
    Task WrapTaskExecutionAsync(TaskContext ctx, Func<Task> execute)
        => execute();
}
