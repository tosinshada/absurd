using System.Data;
using System.Data.Common;
using System.Text.Json;
using Absurd.Internal;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Absurd;

/// <summary>
/// Context object passed to every task handler. Provides step checkpointing,
/// sleep, event waiting, heart beating, and event emission.
/// </summary>
public sealed class TaskContext
{
    private readonly ILogger _log;
    private readonly DbConnection _con;
    private readonly DbTransaction? _tx;
    private readonly string _queueName;
    private readonly ClaimedTask _task;
    private readonly Dictionary<string, JsonElement> _checkpointCache;
    private readonly Dictionary<string, int> _stepNameCounter = new();
    private readonly int _claimTimeout;
    private readonly Action<int> _onLeaseExtended;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private TaskContext(
        ILogger log,
        DbConnection con,
        DbTransaction? tx,
        string queueName,
        ClaimedTask task,
        Dictionary<string, JsonElement> checkpointCache,
        int claimTimeout,
        Action<int> onLeaseExtended)
    {
        _log = log;
        _con = con;
        _tx = tx;
        _queueName = queueName;
        _task = task;
        _checkpointCache = checkpointCache;
        _claimTimeout = claimTimeout;
        _onLeaseExtended = onLeaseExtended;
    }

    /// <summary>The unique identifier of the current task.</summary>
    public Guid TaskId => _task.TaskId;

    /// <summary>
    /// Read-only JSON headers attached to the task.
    /// Returns an empty dictionary when no headers were set.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Headers { get; private set; }
        = new Dictionary<string, JsonElement>();

    // -------------------------------------------------------------------------
    // Factory
    // -------------------------------------------------------------------------

    internal static async Task<TaskContext> CreateAsync(
        ILogger log,
        DbConnection con,
        DbTransaction? tx,
        string queueName,
        ClaimedTask task,
        int claimTimeout,
        Action<int> onLeaseExtended,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(con, ct);

        // Load all existing checkpoints for this run into the cache up-front.
        var cache = new Dictionary<string, JsonElement>();
        await using var cmd = CreateCommand(con, tx,
            "SELECT checkpoint_name, state FROM absurd.get_task_checkpoint_states($1, $2, $3)",
            queueName, task.TaskId, task.RunId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var name = reader.GetString(0);
            var state = reader.GetFieldValue<JsonElement>(1);
            cache[name] = state;
        }

        var ctx = new TaskContext(log, con, tx, queueName, task, cache, claimTimeout, onLeaseExtended);

        // Populate Headers from the ClaimedTask
        if (task.Headers is { ValueKind: JsonValueKind.Object })
        {
            var headers = new Dictionary<string, JsonElement>();
            foreach (var prop in task.Headers.Value.EnumerateObject())
                headers[prop.Name] = prop.Value.Clone();
            ctx.Headers = headers;
        }

        return ctx;
    }

    // -------------------------------------------------------------------------
    // Step API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Runs an idempotent step. The return value is cached in Postgres — on retries
    /// the cached value is returned without calling <paramref name="fn"/> again.
    /// </summary>
    public async Task<T> StepAsync<T>(string name, Func<Task<T>> fn)
    {
        var handle = await BeginStepAsync<T>(name);
        if (handle.IsDone)
            return handle.State!;

        var result = await fn();
        return await CompleteStepAsync(handle, result);
    }

    /// <summary>
    /// Begins a step and checks whether its checkpoint already exists.
    /// Use together with <see cref="CompleteStepAsync{T}"/> when you need to
    /// split step handling into two calls.
    /// </summary>
    public Task<StepHandle<T>> BeginStepAsync<T>(string name)
    {
        var checkpointName = GetCheckpointName(name);

        if (_checkpointCache.TryGetValue(checkpointName, out var cached))
        {
            var state = cached.Deserialize<T>(JsonOptions);
            return Task.FromResult(new StepHandle<T>
            {
                Name = name,
                CheckpointName = checkpointName,
                IsDone = true,
                State = state,
            });
        }

        return Task.FromResult(new StepHandle<T>
        {
            Name = name,
            CheckpointName = checkpointName,
            IsDone = false,
        });
    }

    /// <summary>
    /// Completes a step started with <see cref="BeginStepAsync{T}"/> by persisting its state.
    /// If the handle is already done, returns the cached value without writing.
    /// </summary>
    public async Task<T> CompleteStepAsync<T>(StepHandle<T> handle, T value)
    {
        if (handle.IsDone)
            return handle.State!;

        await PersistCheckpointAsync(handle.CheckpointName, value);
        return value;
    }

    // -------------------------------------------------------------------------
    // Sleep
    // -------------------------------------------------------------------------

    /// <summary>Suspends the task for the specified duration.</summary>
    public Task SleepForAsync(string stepName, TimeSpan duration)
        => SleepUntilAsync(stepName, DateTimeOffset.UtcNow.Add(duration));

    /// <summary>Suspends the task until the specified absolute UTC time.</summary>
    public async Task SleepUntilAsync(string stepName, DateTimeOffset wakeAt)
    {
        var checkpointName = GetCheckpointName(stepName);

        DateTimeOffset actualWakeAt = wakeAt;
        if (_checkpointCache.TryGetValue(checkpointName, out var cached))
        {
            actualWakeAt = cached.Deserialize<DateTimeOffset>(JsonOptions);
        }
        else
        {
            await PersistCheckpointAsync(checkpointName, wakeAt);
        }

        if (DateTimeOffset.UtcNow < actualWakeAt)
        {
            await ScheduleRunAsync(actualWakeAt);
            throw new SuspendTaskException();
        }
    }

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    /// <summary>
    /// Suspends until the named event is emitted on the task's queue.
    /// Returns the event payload. Throws <see cref="AbsurdTimeoutException"/>
    /// if <paramref name="timeoutSeconds"/> elapses first.
    /// </summary>
    public async Task<JsonElement> AwaitEventAsync(
        string eventName,
        string? stepName = null,
        double? timeoutSeconds = null)
    {
        var resolvedStep = stepName ?? $"$awaitEvent:{eventName}";
        int? timeoutInt = timeoutSeconds.HasValue ? (int)Math.Floor(timeoutSeconds.Value) : null;
        var checkpointName = GetCheckpointName(resolvedStep);

        if (_checkpointCache.TryGetValue(checkpointName, out var cached))
            return cached;

        // Check if we woke due to a timeout on this event
        if (_task.WakeEvent == eventName && _task.EventPayload is null)
            throw new AbsurdTimeoutException($"Timed out waiting for event \"{eventName}\"");

        await using var cmd = CreateCommand(_con, _tx,
            "SELECT should_suspend, payload FROM absurd.await_event($1, $2, $3, $4, $5, $6)",
            _queueName, _task.TaskId, _task.RunId, checkpointName, eventName, (object?)timeoutInt ?? DBNull.Value);

        await using var reader = await ExecuteReaderWithStateCheckAsync(cmd);
        if (!await reader.ReadAsync())
            throw new InvalidOperationException("await_event returned no rows.");

        var shouldSuspend = reader.GetBoolean(0);
        var payload = reader.IsDBNull(1) ? default : reader.GetFieldValue<JsonElement>(1);

        if (!shouldSuspend)
        {
            _checkpointCache[checkpointName] = payload;
            return payload;
        }

        throw new SuspendTaskException();
    }

    /// <summary>
    /// Emits a named event on the task's queue. First emit per name wins (idempotent).
    /// </summary>
    public async Task EmitEventAsync(string eventName, object? payload = null)
    {
        if (string.IsNullOrEmpty(eventName))
            throw new ArgumentException("eventName must not be empty.", nameof(eventName));

        var payloadJson = payload is null ? "null" : JsonSerializer.Serialize(payload, JsonOptions);
        // No UUID params — payloadJson is `jsonb` but Postgres accepts text for jsonb assignment.
        await using var cmd = CreateCommand(_con, _tx,
            "SELECT absurd.emit_event($1, $2, $3::jsonb)",
            _queueName, eventName, payloadJson);
        await cmd.ExecuteNonQueryAsync();
    }

    // -------------------------------------------------------------------------
    // Heartbeat
    // -------------------------------------------------------------------------

    /// <summary>
    /// Extends the current run's lease. Defaults to the original claim timeout.
    /// </summary>
    public async Task HeartbeatAsync(int? seconds = null)
    {
        var lease = seconds ?? _claimTimeout;
        await using var cmd = CreateCommand(_con, _tx,
            "SELECT absurd.extend_claim($1, $2, $3)",
            _queueName, _task.RunId, lease);
        await ExecuteNonQueryWithStateCheckAsync(cmd);
        _onLeaseExtended(lease);
    }

    // -------------------------------------------------------------------------
    // Await another task's result (cross-queue, durable)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Durably waits for another task's terminal result from inside a running task.
    /// The wait is checkpointed as a step. <paramref name="queue"/> MUST differ from
    /// the current task's queue.
    /// </summary>
    public async Task<TaskSnapshot> AwaitTaskResultAsync(
        string taskId,
        string queue,
        string? stepName = null,
        double? timeoutSeconds = null)
    {
        if (queue == _queueName)
            throw new InvalidOperationException(
                "TaskContext.AwaitTaskResultAsync cannot wait on tasks in the same queue — " +
                "this can deadlock workers. Spawn the child in a different queue.");

        var resolvedStep = stepName ?? $"$awaitTaskResult:{taskId}";

        return await StepAsync<TaskSnapshot>(resolvedStep, async () =>
        {
            var intervalMs = Math.Max(500, (_claimTimeout * 1000) / 2);
            var nextHeartbeat = DateTimeOffset.UtcNow.AddMilliseconds(intervalMs);
            var deadline = timeoutSeconds.HasValue
                ? DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds.Value)
                : (DateTimeOffset?)null;

            while (true)
            {
                var snapshot = await FetchRemoteTaskSnapshotAsync(queue, taskId);
                if (snapshot?.IsTerminal == true)
                    return snapshot;

                if (deadline.HasValue && DateTimeOffset.UtcNow >= deadline.Value)
                    throw new AbsurdTimeoutException($"Timed out waiting for task {taskId}");

                if (DateTimeOffset.UtcNow >= nextHeartbeat)
                {
                    await HeartbeatAsync();
                    nextHeartbeat = DateTimeOffset.UtcNow.AddMilliseconds(intervalMs);
                }

                await Task.Delay(250);
            }
        });
    }

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    private string GetCheckpointName(string name)
    {
        var count = _stepNameCounter.GetValueOrDefault(name) + 1;
        _stepNameCounter[name] = count;
        return count == 1 ? name : $"{name}#{count}";
    }

    private async Task PersistCheckpointAsync<T>(string checkpointName, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        await using var cmd = CreateCommand(_con, _tx,
            "SELECT absurd.set_task_checkpoint_state($1, $2, $3, $4::jsonb, $5, $6)",
            _queueName, _task.TaskId, checkpointName, json, _task.RunId, _claimTimeout);
        await ExecuteNonQueryWithStateCheckAsync(cmd);
        _checkpointCache[checkpointName] = JsonDocument.Parse(json).RootElement.Clone();
        _onLeaseExtended(_claimTimeout);
    }

    private async Task ScheduleRunAsync(DateTimeOffset wakeAt)
    {
        await using var cmd = CreateCommand(_con, _tx,
            "SELECT absurd.schedule_run($1, $2, $3)",
            _queueName, _task.RunId, wakeAt.UtcDateTime);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<TaskSnapshot?> FetchRemoteTaskSnapshotAsync(string queue, string taskId)
    {
        await using var cmd = CreateCommand(_con, _tx,
            "SELECT state, result, failure_reason FROM absurd.get_task_result($1, $2)",
            queue, Guid.Parse(taskId));
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        var state = reader.GetString(0);
        JsonElement? result = reader.IsDBNull(1) ? null : reader.GetFieldValue<JsonElement>(1);
        JsonElement? failure = reader.IsDBNull(2) ? null : reader.GetFieldValue<JsonElement>(2);

        return new TaskSnapshot { State = state, Result = result, Failure = failure };
    }

    private async Task ExecuteNonQueryWithStateCheckAsync(DbCommand cmd)
    {
        try
        {
            await cmd.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "AB001")
        {
            throw new TaskCancelledException();
        }
        catch (PostgresException ex) when (ex.SqlState == "AB002")
        {
            throw new FailedTaskException();
        }
    }

    private async Task<DbDataReader> ExecuteReaderWithStateCheckAsync(DbCommand cmd)
    {
        try
        {
            return await cmd.ExecuteReaderAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "AB001")
        {
            throw new TaskCancelledException();
        }
        catch (PostgresException ex) when (ex.SqlState == "AB002")
        {
            throw new FailedTaskException();
        }
    }

    private static async Task EnsureOpenAsync(DbConnection con, CancellationToken ct)
    {
        if (con.State != ConnectionState.Open)
            await con.OpenAsync(ct);
    }

    internal static DbCommand CreateCommand(DbConnection con, DbTransaction? tx, string sql, params object?[] parameters)
    {
        var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        if (tx is not null)
            cmd.Transaction = tx;

        foreach (var p in parameters)
        {
            var param = cmd.CreateParameter();
            param.Value = p ?? DBNull.Value;
            cmd.Parameters.Add(param);
        }

        return cmd;
    }
}
