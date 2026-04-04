using System.Data.Common;
using Absurd.Options;

namespace Absurd;

/// <summary>
/// Defines the public contract of the Absurd SDK client.
/// Consume this interface for dependency injection and unit-test substitution.
/// Obtain an instance via <see cref="AbsurdClient"/> or by registering with
/// <c>services.AddAbsurd(...)</c>.
/// </summary>
public interface IAbsurdClient
{
    // -------------------------------------------------------------------------
    // Spawn
    // -------------------------------------------------------------------------

    /// <summary>
    /// Spawns a new task and returns a <see cref="SpawnResult"/>.
    /// </summary>
    Task<SpawnResult> SpawnAsync<TParams>(
        string taskName,
        TParams @params,
        SpawnOptions? options = null,
        CancellationToken ct = default);

    // -------------------------------------------------------------------------
    // Fetch / await task result
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the current snapshot of a task, or <c>null</c> if no such task exists.
    /// </summary>
    Task<TaskSnapshot?> FetchTaskResultAsync(string taskId, CancellationToken ct = default);

    /// <summary>
    /// Polls the task until it reaches a terminal state and returns the final snapshot.
    /// Throws <see cref="AbsurdTimeoutException"/> when <paramref name="timeoutSeconds"/> elapses.
    /// </summary>
    Task<TaskSnapshot> AwaitTaskResultAsync(
        string taskId,
        double? timeoutSeconds = null,
        double pollIntervalSeconds = 0.5,
        CancellationToken ct = default);

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    /// <summary>
    /// Emits a named event on the specified queue. First emit wins — idempotent.
    /// </summary>
    Task EmitEventAsync(
        string eventName,
        object? payload = null,
        string? queueName = null,
        CancellationToken ct = default);

    // -------------------------------------------------------------------------
    // Cancel / Retry
    // -------------------------------------------------------------------------

    /// <summary>Cancels the specified task.</summary>
    Task CancelTaskAsync(
        string taskId,
        string? queueName = null,
        CancellationToken ct = default);

    /// <summary>
    /// Re-queues a failed task and returns the updated <see cref="SpawnResult"/>.
    /// </summary>
    Task<SpawnResult> RetryTaskAsync(
        string taskId,
        RetryTaskOptions? options = null,
        string? queueName = null,
        CancellationToken ct = default);

    // -------------------------------------------------------------------------
    // Connection binding
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a new client that routes all operations through
    /// <paramref name="connection"/> and optional <paramref name="transaction"/>.
    /// The caller owns the connection's lifecycle.
    /// </summary>
    IAbsurdClient BindToConnection(DbConnection connection, DbTransaction? transaction = null);

    // -------------------------------------------------------------------------
    // Task registration
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers a typed task handler. The handler is invoked by the worker
    /// when a task with <paramref name="name"/> is claimed.
    /// </summary>
    void RegisterTask<TParams>(
        string name,
        Func<TParams, TaskContext, Task> handler,
        TaskRegistrationOptions? options = null);

    // -------------------------------------------------------------------------
    // Worker
    // -------------------------------------------------------------------------

    /// <summary>
    /// Starts a long-lived background worker that claims and executes tasks from the queue.
    /// Returns a handle that can be used to stop the worker.
    /// </summary>
    Task<AbsurdWorker> StartWorkerAsync(WorkerOptions? options = null);
}
