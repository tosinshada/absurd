using System.Data;
using System.Data.Common;
using System.Text.Json;
using Absurd.Internal;
using Absurd.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Absurd;

/// <summary>
/// The Absurd SDK client. Create one instance per application and keep it for the
/// lifetime of the process.
/// </summary>
public sealed class AbsurdClient : IDisposable, IAsyncDisposable, IAbsurdClient
{
    private readonly NpgsqlDataSource? _ownedDataSource;
    private readonly NpgsqlDataSource? _externalDataSource;

    // For bind-to-connection scenarios only
    private readonly DbConnection? _boundConnection;
    private readonly DbTransaction? _boundTransaction;

    /// <summary>
    /// Gets the name of the queue associated with this instance.
    /// </summary>
    public readonly string QueueName;

    internal readonly int DefaultMaxAttempts;
    internal readonly ILogger Log;
    internal readonly IAbsurdHooks? Hooks;

    private readonly Dictionary<string, RegisteredTask> _registry = new();
    private AbsurdWorker? _worker;

    internal static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <summary>Creates a new <see cref="AbsurdClient"/> from <see cref="AbsurdOptions"/>.</summary>
    public AbsurdClient(AbsurdOptions? options = null)
    {
        options ??= new AbsurdOptions();

        QueueName = ValidateQueueName(options.QueueName);
        DefaultMaxAttempts = options.DefaultMaxAttempts;
        Log = options.Log ?? NullLogger.Instance;
        Hooks = options.Hooks;

        if (options.DataSource is not null)
        {
            _externalDataSource = options.DataSource;
        }
        else
        {
            var connectionString =
                options.ConnectionString
                ?? Environment.GetEnvironmentVariable("ABSURD_DATABASE_URL")
                ?? Environment.GetEnvironmentVariable("PGDATABASE")
                ?? "postgresql://localhost/absurd";

            _ownedDataSource = NpgsqlDataSource.Create(connectionString);
        }
    }

    private AbsurdClient(
        AbsurdClient parent,
        DbConnection boundConnection,
        DbTransaction? boundTransaction)
    {
        QueueName = parent.QueueName;
        DefaultMaxAttempts = parent.DefaultMaxAttempts;
        Log = parent.Log;
        Hooks = parent.Hooks;
        _registry = parent._registry;
        _boundConnection = boundConnection;
        _boundTransaction = boundTransaction;
    }

    // -------------------------------------------------------------------------
    // Task Registration (phase 5)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers a typed task handler. The handler is invoked by the worker
    /// when a task with <paramref name="name"/> is claimed.
    /// </summary>
    public void RegisterTask<TParams>(
        string name,
        Func<TParams, TaskContext, Task> handler,
        TaskRegistrationOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var queue = ValidateQueueName(options?.Queue ?? QueueName);
        var maxAttempts = options?.DefaultMaxAttempts;
        if (maxAttempts is < 1)
            throw new ArgumentException("DefaultMaxAttempts must be at least 1.", nameof(options));

        _registry[name] = new RegisteredTask
        {
            Name = name,
            Queue = queue,
            DefaultMaxAttempts = maxAttempts,
            DefaultCancellation = options?.DefaultCancellation,
            Handler = (paramsJson, ctx) =>
            {
                var typedParams = paramsJson.Deserialize<TParams>(JsonOptions)
                    ?? throw new InvalidOperationException($"Failed to deserialize params for task '{name}'.");
                return handler(typedParams, ctx);
            },
        };
    }

    // -------------------------------------------------------------------------
    // Connection helpers (used by all client methods and worker)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a new <see cref="AbsurdClient"/> that routes all operations through
    /// <paramref name="connection"/> and optional <paramref name="transaction"/>.
    /// The caller owns the connection's lifecycle — this client will never close it.
    /// </summary>
    public AbsurdClient BindToConnection(DbConnection connection, DbTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return new AbsurdClient(this, connection, transaction);
    }

    /// <inheritdoc/>
    IAbsurdClient IAbsurdClient.BindToConnection(DbConnection connection, DbTransaction? transaction)
        => BindToConnection(connection, transaction);

    /// <summary>Opens a fresh connection from the data source.</summary>
    internal async Task<(NpgsqlConnection con, bool owned)> OpenConnectionAsync(CancellationToken ct = default)
    {
        if (_boundConnection is NpgsqlConnection bound)
        {
            if (bound.State != ConnectionState.Open)
                await bound.OpenAsync(ct);
            return (bound, owned: false);
        }

        var ds = _ownedDataSource ?? _externalDataSource
            ?? throw new InvalidOperationException("No data source available.");
        return (await ds.OpenConnectionAsync(ct), owned: true);
    }

    internal DbTransaction? CurrentTransaction => _boundTransaction;

    internal bool IsBound => _boundConnection is not null;

    internal Dictionary<string, RegisteredTask> Registry => _registry;

    // -------------------------------------------------------------------------
    // Spawn
    // -------------------------------------------------------------------------

    /// <summary>
    /// Spawns a new task, optionally applying the <see cref="IAbsurdHooks.BeforeSpawnAsync"/> hook.
    /// </summary>
    public async Task<SpawnResult> SpawnAsync<TParams>(
        string taskName,
        TParams @params,
        SpawnOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskName);
        options ??= new SpawnOptions();

        var paramsJson = JsonSerializer.Serialize(@params, JsonOptions);

        if (Hooks is not null)
        {
            var paramsElement = JsonSerializer.SerializeToElement(@params, JsonOptions);
            options = await Hooks.BeforeSpawnAsync(taskName, paramsElement, options);
        }

        var queue = ValidateQueueName(options.Queue ?? QueueName);
        var optionsJson = BuildSpawnOptionsJson(options);

        var (con, owned) = await OpenConnectionAsync(ct);
        try
        {
            await using var cmd = TaskContext.CreateCommand(con, CurrentTransaction,
                "SELECT task_id, run_id, attempt, created FROM absurd.spawn_task($1, $2, $3::jsonb, $4::jsonb)",
                queue, taskName, paramsJson, optionsJson);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new InvalidOperationException("spawn_task returned no rows.");

            return new SpawnResult
            {
                TaskId  = reader.GetFieldValue<Guid>(0).ToString(),
                RunId   = reader.GetFieldValue<Guid>(1).ToString(),
                Attempt = reader.GetInt32(2),
                Created = reader.GetBoolean(3),
            };
        }
        finally
        {
            if (owned) await con.DisposeAsync();
        }
    }

    // -------------------------------------------------------------------------
    // Fetch / await task result
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the current snapshot of a task, or <c>null</c> if no such task exists.
    /// </summary>
    public Task<TaskSnapshot?> FetchTaskResultAsync(string taskId, CancellationToken ct = default)
        => FetchTaskSnapshotAsync(taskId, QueueName, ct);

    internal async Task<TaskSnapshot?> FetchTaskSnapshotAsync(
        string taskId,
        string queueName,
        CancellationToken ct = default)
    {
        var (con, owned) = await OpenConnectionAsync(ct);
        try
        {
            await using var cmd = TaskContext.CreateCommand(con, CurrentTransaction,
                "SELECT state, result, failure_reason FROM absurd.get_task_result($1, $2)",
                queueName, Guid.Parse(taskId));

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;

            return new TaskSnapshot
            {
                State   = reader.GetString(0),
                Result  = reader.IsDBNull(1) ? null : reader.GetFieldValue<JsonElement>(1),
                Failure = reader.IsDBNull(2) ? null : reader.GetFieldValue<JsonElement>(2),
            };
        }
        finally
        {
            if (owned) await con.DisposeAsync();
        }
    }

    /// <summary>
    /// Polls the task until it reaches a terminal state and returns the final snapshot.
    /// Throws <see cref="AbsurdTimeoutException"/> when <paramref name="timeoutSeconds"/> elapses.
    /// </summary>
    public async Task<TaskSnapshot> AwaitTaskResultAsync(
        string taskId,
        double? timeoutSeconds = null,
        double pollIntervalSeconds = 0.5,
        CancellationToken ct = default)
    {
        var deadline = timeoutSeconds.HasValue
            ? DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds.Value)
            : (DateTimeOffset?)null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var snapshot = await FetchTaskResultAsync(taskId, ct);
            if (snapshot?.IsTerminal == true)
                return snapshot;

            if (deadline.HasValue && DateTimeOffset.UtcNow >= deadline.Value)
                throw new AbsurdTimeoutException($"Timed out waiting for task {taskId}");

            await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), ct);
        }
    }

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    /// <summary>
    /// Emits a named event on the specified queue. First emit wins — idempotent.
    /// </summary>
    public async Task EmitEventAsync(
        string eventName,
        object? payload = null,
        string? queueName = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        var queue = ValidateQueueName(queueName ?? QueueName);
        var payloadJson = payload is null ? "null" : JsonSerializer.Serialize(payload, JsonOptions);

        var (con, owned) = await OpenConnectionAsync(ct);
        try
        {
            await using var cmd = TaskContext.CreateCommand(con, CurrentTransaction,
                "SELECT absurd.emit_event($1, $2, $3::jsonb)",
                queue, eventName, payloadJson);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (owned) await con.DisposeAsync();
        }
    }

    // -------------------------------------------------------------------------
    // Cancel / Retry
    // -------------------------------------------------------------------------

    /// <summary>Cancels the specified task.</summary>
    public async Task CancelTaskAsync(
        string taskId,
        string? queueName = null,
        CancellationToken ct = default)
    {
        var queue = ValidateQueueName(queueName ?? QueueName);

        var (con, owned) = await OpenConnectionAsync(ct);
        try
        {
            await using var cmd = TaskContext.CreateCommand(con, CurrentTransaction,
                "SELECT absurd.cancel_task($1, $2)",
                queue, Guid.Parse(taskId));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (owned) await con.DisposeAsync();
        }
    }

    /// <summary>
    /// Re-queues a failed task and returns the updated <see cref="SpawnResult"/>.
    /// </summary>
    public async Task<SpawnResult> RetryTaskAsync(
        string taskId,
        RetryTaskOptions? options = null,
        string? queueName = null,
        CancellationToken ct = default)
    {
        var queue = ValidateQueueName(queueName ?? QueueName);
        var optionsJson = BuildRetryOptionsJson(options);

        var (con, owned) = await OpenConnectionAsync(ct);
        try
        {
            await using var cmd = TaskContext.CreateCommand(con, CurrentTransaction,
                "SELECT task_id, run_id, attempt, created FROM absurd.retry_task($1, $2, $3::jsonb)",
                queue, Guid.Parse(taskId), optionsJson);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new InvalidOperationException("retry_task returned no rows.");

            return new SpawnResult
            {
                TaskId  = reader.GetFieldValue<Guid>(0).ToString(),
                RunId   = reader.GetFieldValue<Guid>(1).ToString(),
                Attempt = reader.GetInt32(2),
                Created = reader.GetBoolean(3),
            };
        }
        finally
        {
            if (owned) await con.DisposeAsync();
        }
    }

    // -------------------------------------------------------------------------
    // Queue management
    // -------------------------------------------------------------------------

    /// <summary>Creates a new queue.</summary>
    public async Task CreateQueueAsync(string name, CancellationToken ct = default)
    {
        ValidateQueueName(name);
        var (con, owned) = await OpenConnectionAsync(ct);
        try
        {
            await using var cmd = TaskContext.CreateCommand(con, CurrentTransaction,
                "SELECT absurd.create_queue($1)", name);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (owned) await con.DisposeAsync();
        }
    }

    /// <summary>Drops the specified queue and all of its data.</summary>
    public async Task DropQueueAsync(string name, CancellationToken ct = default)
    {
        ValidateQueueName(name);
        var (con, owned) = await OpenConnectionAsync(ct);
        try
        {
            await using var cmd = TaskContext.CreateCommand(con, CurrentTransaction,
                "SELECT absurd.drop_queue($1)", name);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (owned) await con.DisposeAsync();
        }
    }

    /// <summary>Returns the names of all existing queues.</summary>
    public async Task<IReadOnlyList<string>> ListQueuesAsync(CancellationToken ct = default)
    {
        var (con, owned) = await OpenConnectionAsync(ct);
        try
        {
            var result = new List<string>();
            await using var cmd = TaskContext.CreateCommand(con, CurrentTransaction,
                "SELECT queue_name FROM absurd.list_queues()");
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                result.Add(reader.GetString(0));
            return result;
        }
        finally
        {
            if (owned) await con.DisposeAsync();
        }
    }

    // -------------------------------------------------------------------------
    // Worker
    // -------------------------------------------------------------------------

    /// <summary>
    /// Starts a long-lived background worker that claims and executes tasks from the queue.
    /// Returns a handle that can be used to stop the worker.
    /// </summary>
    public Task<AbsurdWorker> StartWorkerAsync(WorkerOptions? options = null)
    {
        var worker = new AbsurdWorker(this, options ?? new WorkerOptions());
        worker.Start();
        _worker = worker;
        return Task.FromResult(worker);
    }

    /// <summary>
    /// Claims and executes up to <paramref name="batchSize"/> tasks in parallel, then returns.
    /// Suitable for serverless or cron-style invocations.
    /// </summary>
    public async Task WorkBatchAsync(
        string? workerId = null,
        int claimTimeoutSeconds = 120,
        int batchSize = 1,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        var resolvedWorkerId = workerId ?? $"{Environment.MachineName}:{Environment.ProcessId}";
        var claimed = await ClaimTasksAsync(resolvedWorkerId, claimTimeoutSeconds, batchSize, cancellationToken);
        if (claimed.Count == 0)
            return;

        // Run claimed tasks to completion. Use CancellationToken.None so that in-flight
        // tasks are not interrupted when the caller's token fires — the spec says
        // "returns after current in-flight tasks complete".
        var executions = claimed.Select(t =>
            ExecuteClaimedTaskAsync(t, claimTimeoutSeconds, false, null, CancellationToken.None));
        await Task.WhenAll(executions);
    }

    // -------------------------------------------------------------------------
    // Internal: claim + execute (used by WorkBatchAsync and AbsurdWorker)
    // -------------------------------------------------------------------------

    internal async Task<IReadOnlyList<ClaimedTask>> ClaimTasksAsync(
        string workerId,
        int claimTimeoutSeconds,
        int batchSize,
        CancellationToken ct = default)
    {
        var (con, owned) = await OpenConnectionAsync(ct);
        try
        {
            var result = new List<ClaimedTask>();
            await using var cmd = TaskContext.CreateCommand(con, CurrentTransaction,
                "SELECT run_id, task_id, attempt, task_name, params, retry_strategy," +
                " max_attempts, headers, wake_event, event_payload" +
                " FROM absurd.claim_task($1, $2, $3, $4)",
                QueueName, workerId, claimTimeoutSeconds, batchSize);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(new ClaimedTask
                {
                    RunId         = reader.GetFieldValue<Guid>(0),
                    TaskId        = reader.GetFieldValue<Guid>(1),
                    Attempt       = reader.GetInt32(2),
                    TaskName      = reader.GetString(3),
                    Params        = reader.IsDBNull(4) ? default : reader.GetFieldValue<JsonElement>(4),
                    RetryStrategy = reader.IsDBNull(5) ? null    : reader.GetFieldValue<JsonElement>(5),
                    MaxAttempts   = reader.IsDBNull(6) ? null    : reader.GetInt32(6),
                    Headers       = reader.IsDBNull(7) ? null    : reader.GetFieldValue<JsonElement>(7),
                    WakeEvent     = reader.IsDBNull(8) ? null    : reader.GetString(8),
                    EventPayload  = reader.IsDBNull(9) ? null    : reader.GetFieldValue<JsonElement>(9),
                });
            }
            return result;
        }
        finally
        {
            if (owned) await con.DisposeAsync();
        }
    }

    internal async Task ExecuteClaimedTaskAsync(
        ClaimedTask claimed,
        int claimTimeoutSeconds,
        bool fatalOnLeaseTimeout,
        Func<Exception, Task>? onError,
        CancellationToken ct = default)
    {
        if (IsBound)
            throw new InvalidOperationException("Cannot execute tasks on a connection-bound client.");

        var ds = _ownedDataSource ?? _externalDataSource
            ?? throw new InvalidOperationException("No data source available.");

        await using var con = await ds.OpenConnectionAsync(ct);

        // Watchdog: exit the process if a task holds its lease for more than 2× the timeout.
        using var watchdogCts = fatalOnLeaseTimeout
            ? new CancellationTokenSource(TimeSpan.FromSeconds(2 * claimTimeoutSeconds))
            : null;

        watchdogCts?.Token.Register(() =>
        {
            Log.LogCritical(
                "[absurd] Task {TaskId}/{RunId} exceeded 2\u00d7 claim timeout — terminating process",
                claimed.TaskId, claimed.RunId);
            Environment.Exit(1);
        });

        void OnLeaseExtended(int leaseSeconds) =>
            watchdogCts?.CancelAfter(TimeSpan.FromSeconds(2 * leaseSeconds));

        try
        {
            var ctx = await TaskContext.CreateAsync(
                Log, con, null, QueueName, claimed,
                claimTimeoutSeconds, OnLeaseExtended, ct);

            if (!_registry.TryGetValue(claimed.TaskName, out var registration))
                throw new InvalidOperationException(
                    $"No handler registered for task '{claimed.TaskName}'.");

            if (registration.Queue != QueueName)
                throw new InvalidOperationException(
                    $"Task '{claimed.TaskName}' is registered on queue '{registration.Queue}' " +
                    $"but was claimed from queue '{QueueName}'.");

            async Task Execute()
            {
                await registration.Handler(claimed.Params, ctx);
                await using var completeCmd = TaskContext.CreateCommand(con, null,
                    "SELECT absurd.complete_run($1, $2)",
                    QueueName, claimed.RunId);
                await completeCmd.ExecuteNonQueryAsync(ct);
            }

            if (Hooks is not null)
                await Hooks.WrapTaskExecutionAsync(ctx, Execute);
            else
                await Execute();
        }
        catch (SuspendTaskException)
        {
            // Normal control flow — task suspended (sleep / await-event).
        }
        catch (TaskCancelledException)
        {
            // Normal control flow — task was cancelled.
        }
        catch (FailedTaskException)
        {
            // The run was already failed by the DB (e.g. lease expired).
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "[absurd] Task {TaskId} failed on attempt {Attempt}",
                claimed.TaskId, claimed.Attempt);

            if (onError is not null)
                try { await onError(ex); } catch { /* swallow hook errors */ }

            try
            {
                var reason = JsonSerializer.Serialize(new
                {
                    name       = ex.GetType().Name,
                    message    = ex.Message,
                    stackTrace = ex.StackTrace,
                }, JsonOptions);

                await using var failCmd = TaskContext.CreateCommand(con, null,
                    "SELECT absurd.fail_run($1, $2, $3::jsonb)",
                    QueueName, claimed.RunId, reason);
                await failCmd.ExecuteNonQueryAsync(ct);
            }
            catch (Exception failEx)
            {
                Log.LogError(failEx, "[absurd] Could not mark run {RunId} as failed", claimed.RunId);
            }
        }
    }

    // -------------------------------------------------------------------------
    // JSON serialisation helpers
    // -------------------------------------------------------------------------

    private string BuildSpawnOptionsJson(SpawnOptions options)
    {
        using var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms);
        writer.WriteStartObject();

        if (options.Headers is { Count: > 0 } headers)
        {
            writer.WritePropertyName("headers");
            writer.WriteStartObject();
            foreach (var (k, v) in headers)
            {
                writer.WritePropertyName(k);
                v.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        if (options.MaxAttempts.HasValue)
            writer.WriteNumber("max_attempts", options.MaxAttempts.Value);

        if (options.RetryStrategy is not null)
        {
            writer.WritePropertyName("retry_strategy");
            WriteRetryStrategy(writer, options.RetryStrategy);
        }

        if (options.Cancellation is not null)
        {
            writer.WritePropertyName("cancellation");
            WriteCancellationPolicy(writer, options.Cancellation);
        }

        if (options.IdempotencyKey is not null)
            writer.WriteString("idempotency_key", options.IdempotencyKey);

        writer.WriteEndObject();
        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string BuildRetryOptionsJson(RetryTaskOptions? options)
    {
        if (options is null) return "{}";

        using var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms);
        writer.WriteStartObject();

        if (options.MaxAttempts.HasValue)
            writer.WriteNumber("max_attempts", options.MaxAttempts.Value);

        if (options.SpawnNewTask)
            writer.WriteBoolean("spawn_new", true);

        writer.WriteEndObject();
        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteRetryStrategy(Utf8JsonWriter writer, RetryStrategy strategy)
    {
        writer.WriteStartObject();
        switch (strategy)
        {
            case FixedRetryStrategy f:
                writer.WriteString("kind", "fixed");
                writer.WriteNumber("base_seconds", f.BaseSeconds);
                break;
            case ExponentialRetryStrategy e:
                writer.WriteString("kind", "exponential");
                writer.WriteNumber("base_seconds", e.BaseSeconds);
                writer.WriteNumber("factor", e.Factor);
                if (e.MaxSeconds.HasValue)
                    writer.WriteNumber("max_seconds", e.MaxSeconds.Value);
                break;
            case NoRetryStrategy:
                writer.WriteString("kind", "none");
                break;
        }
        writer.WriteEndObject();
    }

    private static void WriteCancellationPolicy(Utf8JsonWriter writer, CancellationPolicy policy)
    {
        writer.WriteStartObject();
        if (policy.MaxDuration.HasValue)
            writer.WriteNumber("max_duration", policy.MaxDuration.Value);
        if (policy.MaxDelay.HasValue)
            writer.WriteNumber("max_delay", policy.MaxDelay.Value);
        writer.WriteEndObject();
    }

    // -------------------------------------------------------------------------
    // Dispose
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>
    /// Prefer <see cref="DisposeAsync"/> where possible. This synchronous overload performs a
    /// best-effort shutdown: it signals the worker to stop and disposes the data source
    /// synchronously, but does not block waiting for in-flight tasks to drain.
    /// </remarks>
    public void Dispose()
    {
        _worker?.Dispose();
        // Never dispose a bound connection — the caller owns it.
        _ownedDataSource?.Dispose();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_worker is not null)
            await _worker.DisposeAsync();
        // Never dispose a bound connection — the caller owns it.
        if (_ownedDataSource is not null)
            await _ownedDataSource.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // Validation
    // -------------------------------------------------------------------------

    private const int MaxQueueNameBytes = 57;

    internal static string ValidateQueueName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Queue name must not be empty.");
        if (System.Text.Encoding.UTF8.GetByteCount(name) > MaxQueueNameBytes)
            throw new ArgumentException($"Queue name \"{name}\" is too long (max {MaxQueueNameBytes} bytes).");
        return name;
    }
}
