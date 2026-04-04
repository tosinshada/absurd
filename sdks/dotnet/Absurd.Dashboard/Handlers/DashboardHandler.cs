using System.Text.Json;
using Absurd.Dashboard.Internal;
using Absurd.Dashboard.Models;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace Absurd.Dashboard.Handlers;

/// <summary>
/// Handles all HTTP requests within the Absurd Dashboard branch.
/// Instantiated as a singleton; all public state is immutable after construction.
/// </summary>
internal sealed class DashboardHandler
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IndexHtmlRenderer _renderer;
    private readonly TaskNameCache _taskNameCache;

    public DashboardHandler(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
        _renderer = new IndexHtmlRenderer();
        _taskNameCache = new TaskNameCache();
    }

    // =========================================================================
    // Main router — analogous to Go's route mux
    // =========================================================================

    /// <summary>
    /// Routes an incoming request to the appropriate handler based on the path.
    /// Must be called after the path prefix has been stripped by <c>app.Map</c>.
    /// </summary>
    public async Task HandleAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";

        // Internal endpoints
        if (path == "/_healthz")
        {
            await HandleHealthzAsync(context);
            return;
        }

        // Static assets
        if (path.StartsWith("/_static/", StringComparison.OrdinalIgnoreCase))
        {
            var assetPath = path["/_static".Length..];
            if (!await EmbeddedStaticHandler.TryServeAsync(context, assetPath))
                context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // API: specific routes before prefix routes
        if (path == "/api/config")
        {
            await HandleConfigAsync(context); return;
        }

        if (path == "/api/metrics")
        {
            await HandleMetricsAsync(context); return;
        }

        if (path == "/api/events")
        {
            await HandleEventsAsync(context); return;
        }

        if (path == "/api/tasks/retry")
        {
            await HandleRetryTaskAsync(context); return;
        }

        if (path == "/api/tasks")
        {
            await HandleTasksAsync(context); return;
        }

        if (path.StartsWith("/api/tasks/", StringComparison.OrdinalIgnoreCase))
        {
            await HandleTaskDetailAsync(context);
            return;
        }

        if (path == "/api/queues")
        {
            await HandleQueuesAsync(context); return;
        }

        if (path.StartsWith("/api/queues/", StringComparison.OrdinalIgnoreCase))
        {
            await HandleQueueResourceAsync(context);
            return;
        }

        // Redirect bare mount path to trailing slash
        if (path == "")
        {
            var runtimeCfg = PathHelpers.BuildRuntimeConfig(context.Request);
            var basePath = string.IsNullOrEmpty(runtimeCfg.BasePath) ? "/" : runtimeCfg.BasePath + "/";
            context.Response.Redirect(basePath, permanent: true);
            return;
        }

        // SPA fallback — any other path serves index.html
        await HandleSpaAsync(context);
    }

    // =========================================================================
    // /_healthz
    // =========================================================================

    private async Task HandleHealthzAsync(HttpContext context)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(context.RequestAborted);
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync("ok");
        }
        catch
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync("database unavailable");
        }
    }

    // =========================================================================
    // /api/config
    // =========================================================================

    private Task HandleConfigAsync(HttpContext context)
    {
        var config = PathHelpers.BuildRuntimeConfig(context.Request);
        return ResponseHelpers.WriteJsonAsync(context.Response, StatusCodes.Status200OK, config);
    }

    // =========================================================================
    // SPA index (fallback)
    // =========================================================================

    private async Task HandleSpaAsync(HttpContext context)
    {
        if (context.Request.Method != HttpMethods.Get && context.Request.Method != HttpMethods.Head)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var config = PathHelpers.BuildRuntimeConfig(context.Request);
        await _renderer.TrySendAsync(context, config);
    }

    // =========================================================================
    // /api/metrics
    // =========================================================================

    private async Task HandleMetricsAsync(HttpContext context)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        var ct = cts.Token;

        List<string> queueNames;
        try { queueNames = await ListQueueNamesAsync(ct); }
        catch { await ResponseHelpers.WriteErrorAsync(context.Response, 500, "failed to query queues"); return; }

        var metrics = new List<QueueMetrics>();
        var now = DateTime.UtcNow;

        foreach (var queueName in queueNames)
        {
            var ttable = QueueHelpers.QueueTableIdentifier("t", queueName);
            var rtable = QueueHelpers.QueueTableIdentifier("r", queueName);

            var sql = $"""
                SELECT
                    COUNT(*) AS total_tasks,
                    COUNT(*) FILTER (WHERE t.state IN ('pending', 'sleeping')) AS queued_tasks,
                    COUNT(*) FILTER (WHERE t.state = 'pending' AND r.available_at <= NOW()) AS visible_tasks,
                    MIN(CASE WHEN t.state IN ('pending', 'sleeping') THEN r.created_at END) AS oldest_at,
                    MAX(CASE WHEN t.state IN ('pending', 'sleeping') THEN r.created_at END) AS newest_at
                FROM absurd.{ttable} t
                LEFT JOIN absurd.{rtable} r ON r.task_id = t.task_id AND r.run_id = t.last_attempt_run
                """;

            try
            {
                await using var conn = await _dataSource.OpenConnectionAsync(ct);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct)) continue;

                metrics.Add(new QueueMetrics
                {
                    QueueName = queueName,
                    TotalMessages = reader.GetInt64(0),
                    QueueLength = reader.GetInt64(1),
                    QueueVisibleLength = reader.GetInt64(2),
                    OldestMsgAt = reader.IsDBNull(3) ? null : reader.GetDateTime(3).ToUniversalTime(),
                    NewestMsgAt = reader.IsDBNull(4) ? null : reader.GetDateTime(4).ToUniversalTime(),
                    ScrapeTime = now,
                });
            }
            catch { /* skip queues with errors, same as Go */ }
        }

        await ResponseHelpers.WriteJsonAsync(context.Response, 200, new { queues = metrics });
    }

    // =========================================================================
    // /api/queues
    // =========================================================================

    private async Task HandleQueuesAsync(HttpContext context)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        var ct = cts.Token;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        await using var queueCmd = conn.CreateCommand();
        queueCmd.CommandText = "SELECT queue_name, created_at FROM absurd.queues ORDER BY queue_name";
        await using var queueReader = await queueCmd.ExecuteReaderAsync(ct);

        var queueNames = new List<(string name, DateTime createdAt)>();
        while (await queueReader.ReadAsync(ct))
            queueNames.Add((queueReader.GetString(0), queueReader.GetDateTime(1).ToUniversalTime()));

        await queueReader.CloseAsync();

        var summaries = new List<QueueSummary>();
        foreach (var (queueName, createdAt) in queueNames)
        {
            var ttable = QueueHelpers.QueueTableIdentifier("t", queueName);
            var countSql = $"""
                SELECT
                    COUNT(*) FILTER (WHERE state = 'pending')   AS pending,
                    COUNT(*) FILTER (WHERE state = 'running')   AS running,
                    COUNT(*) FILTER (WHERE state = 'sleeping')  AS sleeping,
                    COUNT(*) FILTER (WHERE state = 'completed') AS completed,
                    COUNT(*) FILTER (WHERE state = 'failed')    AS failed,
                    COUNT(*) FILTER (WHERE state = 'cancelled') AS cancelled
                FROM absurd.{ttable}
                """;

            try
            {
                await using var countCmd = conn.CreateCommand();
                countCmd.CommandText = countSql;
                await using var r = await countCmd.ExecuteReaderAsync(ct);
                if (!await r.ReadAsync(ct)) continue;

                summaries.Add(new QueueSummary
                {
                    QueueName = queueName,
                    CreatedAt = createdAt,
                    PendingCount = r.GetInt64(0),
                    RunningCount = r.GetInt64(1),
                    SleepingCount = r.GetInt64(2),
                    CompletedCount = r.GetInt64(3),
                    FailedCount = r.GetInt64(4),
                    CancelledCount = r.GetInt64(5),
                });
            }
            catch { /* skip, same as Go */ }
        }

        await ResponseHelpers.WriteJsonAsync(context.Response, 200, summaries);
    }

    // =========================================================================
    // /api/queues/{name}[/tasks|/events]
    // =========================================================================

    private async Task HandleQueueResourceAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        var remainder = path["/api/queues/".Length..].Trim('/');
        if (string.IsNullOrEmpty(remainder))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("queue name required");
            return;
        }

        var parts = remainder.Split('/', 2);
        var queueName = parts[0];

        if (parts.Length < 2)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        switch (parts[1])
        {
            case "tasks":
                await HandleQueueTasksAsync(context, queueName);
                break;
            case "events":
                await HandleQueueEventsAsync(context, queueName);
                break;
            default:
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                break;
        }
    }

    private async Task HandleQueueTasksAsync(HttpContext context, string queueName)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        var ct = cts.Token;

        if (!await EnsureQueueExistsAsync(queueName, ct))
        {
            await ResponseHelpers.WriteErrorAsync(context.Response, 404, "queue not found");
            return;
        }

        var ttable = QueueHelpers.QueueTableIdentifier("t", queueName);
        var rtable = QueueHelpers.QueueTableIdentifier("r", queueName);
        var queueLiteral = QueueHelpers.QuoteLiteral(queueName);

        var sql = $"""
            SELECT
                t.task_id, r.run_id, {queueLiteral} AS queue_name, t.task_name, r.state,
                r.attempt, t.max_attempts, r.created_at,
                COALESCE(r.completed_at, r.failed_at, r.started_at, r.created_at) AS updated_at,
                r.completed_at, r.claimed_by
            FROM absurd.{ttable} t
            JOIN absurd.{rtable} r ON r.task_id = t.task_id
            ORDER BY r.created_at DESC
            """;

        var tasks = await QueryTaskSummariesAsync(sql, includeParams: false, ct);
        await ResponseHelpers.WriteJsonAsync(context.Response, 200, tasks);
    }

    private async Task HandleQueueEventsAsync(HttpContext context, string queueName)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        var ct = cts.Token;

        var limit = QueueHelpers.ParsePositiveInt(context.Request.Query["limit"], 100);
        if (limit > 500) limit = 500;
        var eventName = context.Request.Query["eventName"].ToString().Trim();

        try
        {
            var events = await FetchQueueEventsAsync(queueName, limit, eventName, null, null, ct);
            await ResponseHelpers.WriteJsonAsync(context.Response, 200, events);
        }
        catch
        {
            await ResponseHelpers.WriteErrorAsync(context.Response, 500, "failed to query queue events");
        }
    }

    // =========================================================================
    // /api/tasks
    // =========================================================================

    private async Task HandleTasksAsync(HttpContext context)
    {
        var q = context.Request.Query;
        var search = q["q"].ToString().Trim();
        var isSearch = !string.IsNullOrEmpty(search);

        var timeout = isSearch ? TimeSpan.FromSeconds(120) : TimeSpan.FromSeconds(30);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        cts.CancelAfter(timeout);
        var ct = cts.Token;

        var (statusFilter, statusValid) = QueueHelpers.NormalizeTaskStatusFilter(q["status"]);
        var queueFilter = q["queue"].ToString().Trim();
        var taskNameFilter = q["taskName"].ToString().Trim();
        var taskIdStr = q["taskId"].ToString().Trim();
        var afterTime = QueueHelpers.ParseOptionalTime(q["after"].ToString().Trim());
        var beforeTime = QueueHelpers.ParseOptionalTime(q["before"].ToString().Trim());

        var page = QueueHelpers.ParsePositiveInt(q["page"], 1);
        var perPage = QueueHelpers.ParsePositiveInt(q["perPage"], 25);
        if (perPage > 200) perPage = 200;

        List<string> queueNames;
        try { queueNames = await ListQueueNamesAsync(ct); }
        catch
        {
            await ResponseHelpers.WriteErrorAsync(context.Response, 500, "failed to query queues");
            return;
        }

        if (!statusValid)
        {
            await ResponseHelpers.WriteJsonAsync(context.Response, 200, EmptyTaskListResponse(page, perPage, queueNames));
            return;
        }

        Guid? taskIdFilter = null;
        if (!string.IsNullOrEmpty(taskIdStr))
        {
            if (!Guid.TryParse(taskIdStr, out var parsedTaskId))
            {
                await ResponseHelpers.WriteJsonAsync(context.Response, 200, EmptyTaskListResponse(page, perPage, queueNames));
                return;
            }
            taskIdFilter = parsedTaskId;
        }

        var selectedQueues = queueNames;
        if (!string.IsNullOrEmpty(queueFilter))
        {
            selectedQueues = queueNames.Where(n => n == queueFilter).ToList();
            if (selectedQueues.Count == 0)
            {
                await ResponseHelpers.WriteJsonAsync(context.Response, 200, EmptyTaskListResponse(page, perPage, queueNames));
                return;
            }
        }

        // Collect available task names (from cache)
        var availableTaskNames = new List<string>();
        foreach (var qn in selectedQueues)
        {
            try
            {
                var names = await _taskNameCache.GetOrFetchAsync(_dataSource, qn, ct);
                availableTaskNames.AddRange(names);
            }
            catch { /* continue */ }
        }
        availableTaskNames = availableTaskNames.Distinct(StringComparer.Ordinal).OrderBy(n => n).ToList();

        int start = (page - 1) * perPage;
        int windowSize = start + perPage + 1;
        int limitPerQueue = isSearch ? 0 : windowSize;

        var merged = new List<TaskSummary>();
        bool hasWindowTruncation = false;

        foreach (var queueName in selectedQueues)
        {
            try
            {
                var (candidates, truncated) = await FetchQueueTaskCandidatesAsync(
                    queueName, statusFilter, taskNameFilter, taskIdFilter,
                    limitPerQueue, includeParams: isSearch, afterTime, beforeTime, ct);

                merged.AddRange(candidates);
                if (truncated) hasWindowTruncation = true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
                await context.Response.WriteAsync("task query timed out");
                return;
            }
            catch { /* skip queue, same as Go */ }
        }

        if (isSearch)
        {
            merged = merged
                .Where(t => QueueHelpers.MatchesTaskSearch(
                    t.TaskId.ToString("D"), t.RunId.ToString("D"), t.QueueName, t.TaskName,
                    t.Params.HasValue ? t.Params.Value.GetRawText() : null,
                    search))
                .ToList();
        }

        merged.Sort((a, b) => b.RunId.CompareTo(a.RunId));

        int total = (isSearch || !hasWindowTruncation) ? merged.Count : -1;

        if (start > merged.Count) start = merged.Count;
        int end = Math.Min(start + perPage, merged.Count);

        bool hasMore = merged.Count > end || hasWindowTruncation;
        if (total >= 0) hasMore = end < total;

        await ResponseHelpers.WriteJsonAsync(context.Response, 200, new TaskListResponse
        {
            Items = merged[start..end],
            Total = total,
            HasMore = hasMore,
            Page = page,
            PerPage = perPage,
            AvailableStatuses = [.. QueueHelpers.AllTaskStatuses()],
            AvailableQueues = queueNames,
            AvailableTaskNames = availableTaskNames,
        });
    }

    private async Task<(List<TaskSummary> tasks, bool truncated)> FetchQueueTaskCandidatesAsync(
        string queueName,
        string? statusFilter,
        string taskNameFilter,
        Guid? taskIdFilter,
        int limit,
        bool includeParams,
        DateTime? afterTime,
        DateTime? beforeTime,
        CancellationToken ct)
    {
        var ttable = QueueHelpers.QueueTableIdentifier("t", queueName);
        var rtable = QueueHelpers.QueueTableIdentifier("r", queueName);
        var queueLiteral = QueueHelpers.QuoteLiteral(queueName);
        var paramsSelect = includeParams ? "t.params" : "NULL::jsonb";

        var sql = $"""
            SELECT
                t.task_id, r.run_id, {queueLiteral} AS queue_name, t.task_name, r.state,
                r.attempt, t.max_attempts, r.created_at,
                COALESCE(r.completed_at, r.failed_at, r.started_at, r.created_at) AS updated_at,
                r.completed_at, r.claimed_by, {paramsSelect} AS params
            FROM absurd.{rtable} r
            JOIN absurd.{ttable} t ON t.task_id = r.task_id
            """;

        var clauses = new List<string>();
        var paramValues = new List<object?>();

        if (!string.IsNullOrEmpty(statusFilter))
        {
            paramValues.Add(statusFilter);
            clauses.Add($"r.state = ${paramValues.Count}");
        }
        if (!string.IsNullOrEmpty(taskNameFilter))
        {
            paramValues.Add(taskNameFilter);
            clauses.Add($"t.task_name = ${paramValues.Count}");
        }
        if (taskIdFilter.HasValue)
        {
            paramValues.Add(taskIdFilter.Value);
            clauses.Add($"t.task_id = ${paramValues.Count}");
        }
        if (afterTime.HasValue)
        {
            paramValues.Add(afterTime.Value);
            clauses.Add($"r.created_at >= ${paramValues.Count}");
        }
        if (beforeTime.HasValue)
        {
            paramValues.Add(beforeTime.Value);
            clauses.Add($"r.created_at <= ${paramValues.Count}");
        }

        if (clauses.Count > 0)
            sql += " WHERE " + string.Join(" AND ", clauses);

        sql += " ORDER BY r.run_id DESC";

        int queryLimit = limit;
        if (queryLimit > 0)
        {
            queryLimit += 1;
            paramValues.Add(queryLimit);
            sql += $" LIMIT ${paramValues.Count}";
        }

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var pv in paramValues)
            cmd.Parameters.AddWithValue(pv ?? DBNull.Value);

        var tasks = new List<TaskSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            tasks.Add(ReadTaskSummary(reader, hasParams: includeParams));

        bool truncated = limit > 0 && tasks.Count > limit;
        if (truncated) tasks.RemoveAt(tasks.Count - 1);

        return (tasks, truncated);
    }

    // =========================================================================
    // /api/tasks/{runId}
    // =========================================================================

    private async Task HandleTaskDetailAsync(HttpContext context)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        var ct = cts.Token;

        var runIdStr = context.Request.Path.Value!.Replace("/api/tasks/", "", StringComparison.OrdinalIgnoreCase).TrimStart('/');
        if (!Guid.TryParse(runIdStr, out var runId))
        {
            await ResponseHelpers.WriteErrorAsync(context.Response, 400, "run ID must be a valid UUID");
            return;
        }

        string? queueName;
        try { queueName = await FindQueueForRunAsync(runId, ct); }
        catch { queueName = null; }

        if (queueName == null)
        {
            await ResponseHelpers.WriteErrorAsync(context.Response, 404, "task not found");
            return;
        }

        var ttable = QueueHelpers.QueueTableIdentifier("t", queueName);
        var rtable = QueueHelpers.QueueTableIdentifier("r", queueName);
        var ctable = QueueHelpers.QueueTableIdentifier("c", queueName);
        var wtable = QueueHelpers.QueueTableIdentifier("w", queueName);
        var etable = QueueHelpers.QueueTableIdentifier("e", queueName);
        var queueLiteral = QueueHelpers.QuoteLiteral(queueName);

        var detailSql = $"""
            SELECT
                t.task_id, r.run_id, {queueLiteral} AS queue_name, t.task_name,
                t.state, r.attempt, t.max_attempts,
                t.params, t.retry_strategy, t.headers,
                COALESCE(r.failure_reason, r.result) AS state_detail,
                r.created_at,
                COALESCE(r.completed_at, r.failed_at, r.started_at, r.created_at) AS updated_at,
                r.completed_at, r.claimed_by
            FROM absurd.{ttable} t
            JOIN absurd.{rtable} r ON r.task_id = t.task_id
            WHERE r.run_id = $1
            LIMIT 1
            """;

        TaskDetail? detail;
        Guid taskId = default;
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = detailSql;
            cmd.Parameters.AddWithValue(runId);
            await using var r = await cmd.ExecuteReaderAsync(ct);

            if (!await r.ReadAsync(ct))
            {
                await ResponseHelpers.WriteErrorAsync(context.Response, 404, "task not found");
                return;
            }

            taskId = r.GetGuid(0);
            detail = ReadTaskDetail(r);

            await r.CloseAsync();

            // Checkpoints
            var checkpoints = new List<CheckpointState>();
            var cpSql = $"""
                SELECT checkpoint_name, state, status, owner_run_id, NULL::timestamptz AS expires_at, updated_at
                FROM absurd.{ctable}
                WHERE task_id = $1 AND owner_run_id = $2
                ORDER BY updated_at DESC
                """;
            await using var cpCmd = conn.CreateCommand();
            cpCmd.CommandText = cpSql;
            cpCmd.Parameters.AddWithValue(taskId);
            cpCmd.Parameters.AddWithValue(runId);
            await using var cpReader = await cpCmd.ExecuteReaderAsync(ct);
            while (await cpReader.ReadAsync(ct))
            {
                checkpoints.Add(new CheckpointState
                {
                    StepName = cpReader.GetString(0),
                    State = QueueHelpers.ParseJsonElement(cpReader.IsDBNull(1) ? null : cpReader.GetString(1)),
                    Status = cpReader.GetString(2),
                    OwnerRunId = cpReader.IsDBNull(3) ? null : (Guid?)cpReader.GetGuid(3),
                    ExpiresAt = cpReader.IsDBNull(4) ? null : cpReader.GetDateTime(4).ToUniversalTime(),
                    UpdatedAt = cpReader.GetDateTime(5).ToUniversalTime(),
                });
            }

            await cpReader.CloseAsync();

            // Wait states
            var waits = new List<WaitState>();
            var waitSql = $"""
                SELECT
                    CASE
                        WHEN r.wake_event IS NOT NULL THEN 'event'
                        WHEN r.available_at > NOW() THEN 'timer'
                        ELSE 'none'
                    END AS wait_type,
                    r.available_at, r.wake_event, w.step_name,
                    NULL::jsonb AS payload, r.event_payload,
                    w.created_at, e.emitted_at
                FROM absurd.{rtable} r
                LEFT JOIN absurd.{wtable} w ON w.run_id = r.run_id
                LEFT JOIN absurd.{etable} e ON e.event_name = r.wake_event AND e.payload IS NOT NULL
                WHERE r.run_id = $1 AND r.state = 'sleeping'
                ORDER BY w.created_at DESC
                """;
            await using var wCmd = conn.CreateCommand();
            wCmd.CommandText = waitSql;
            wCmd.Parameters.AddWithValue(runId);

            try
            {
                await using var wReader = await wCmd.ExecuteReaderAsync(ct);
                while (await wReader.ReadAsync(ct))
                {
                    waits.Add(new WaitState
                    {
                        WaitType = wReader.GetString(0),
                        WakeAt = wReader.IsDBNull(1) ? null : wReader.GetDateTime(1).ToUniversalTime(),
                        WakeEvent = wReader.IsDBNull(2) ? null : wReader.GetString(2),
                        StepName = wReader.IsDBNull(3) ? null : wReader.GetString(3),
                        Payload = QueueHelpers.ParseJsonElement(wReader.IsDBNull(4) ? null : wReader.GetString(4)),
                        EventPayload = QueueHelpers.ParseJsonElement(wReader.IsDBNull(5) ? null : wReader.GetString(5)),
                        UpdatedAt = wReader.IsDBNull(6) ? DateTime.UtcNow : wReader.GetDateTime(6).ToUniversalTime(),
                        EmittedAt = wReader.IsDBNull(7) ? null : wReader.GetDateTime(7).ToUniversalTime(),
                    });
                }
            }
            catch { /* wait query is best-effort */ }

            detail = detail with { Checkpoints = checkpoints, Waits = waits };
        }
        catch (Exception)
        {
            await ResponseHelpers.WriteErrorAsync(context.Response, 500, "failed to query task details");
            return;
        }

        await ResponseHelpers.WriteJsonAsync(context.Response, 200, detail);
    }

    // =========================================================================
    // POST /api/tasks/retry
    // =========================================================================

    private async Task HandleRetryTaskAsync(HttpContext context)
    {
        if (context.Request.Method != HttpMethods.Post)
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        RetryTaskRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<RetryTaskRequest>(
                context.Request.Body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch
        {
            await ResponseHelpers.WriteErrorAsync(context.Response, 400, "invalid JSON body");
            return;
        }

        if (request == null || request.TaskId == Guid.Empty)
        {
            await ResponseHelpers.WriteErrorAsync(context.Response, 400, "taskId is required");
            return;
        }
        if (string.IsNullOrWhiteSpace(request.QueueName))
        {
            await ResponseHelpers.WriteErrorAsync(context.Response, 400, "queueName is required");
            return;
        }
        if (request.MaxAttempts.HasValue && request.MaxAttempts < 1)
        {
            await ResponseHelpers.WriteErrorAsync(context.Response, 400, "maxAttempts must be >= 1");
            return;
        }
        if (request.ExtraAttempts.HasValue && request.ExtraAttempts < 1)
        {
            await ResponseHelpers.WriteErrorAsync(context.Response, 400, "extraAttempts must be >= 1");
            return;
        }
        if (request.SpawnNewTask && request.ExtraAttempts.HasValue)
        {
            await ResponseHelpers.WriteErrorAsync(context.Response, 400, "extraAttempts cannot be used when spawnNewTask is true");
            return;
        }
        if (!request.SpawnNewTask && request.MaxAttempts.HasValue)
        {
            await ResponseHelpers.WriteErrorAsync(context.Response, 400, "maxAttempts cannot be used when spawnNewTask is false; use extraAttempts");
            return;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        var ct = cts.Token;

        if (!await EnsureQueueExistsAsync(request.QueueName, ct))
        {
            await ResponseHelpers.WriteErrorAsync(context.Response, 404, "queue not found");
            return;
        }

        // Build retry options JSON (mirrors Go's options map)
        var options = new Dictionary<string, object?>();
        if (request.SpawnNewTask)
        {
            options["spawn_new"] = true;
            if (request.MaxAttempts.HasValue)
                options["max_attempts"] = request.MaxAttempts.Value;
        }
        else if (request.ExtraAttempts.HasValue)
        {
            int currentAttempts;
            try { currentAttempts = await GetTaskAttemptsAsync(request.QueueName, request.TaskId, ct); }
            catch (InvalidOperationException)
            {
                await ResponseHelpers.WriteErrorAsync(context.Response, 404, "task not found in queue");
                return;
            }
            options["max_attempts"] = currentAttempts + request.ExtraAttempts.Value;
        }

        var optionsJson = JsonSerializer.Serialize(options);

        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT task_id, run_id, attempt, created FROM absurd.retry_task($1, $2, $3::jsonb)";
            cmd.Parameters.AddWithValue(request.QueueName);
            cmd.Parameters.AddWithValue(request.TaskId);
            cmd.Parameters.AddWithValue(optionsJson);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                await ResponseHelpers.WriteErrorAsync(context.Response, 500, "failed to retry task");
                return;
            }

            var response = new RetryTaskResponse
            {
                TaskId = reader.GetGuid(0),
                RunId = reader.GetGuid(1),
                Attempt = reader.GetInt32(2),
                Created = reader.GetBoolean(3),
                QueueName = request.QueueName,
            };
            await ResponseHelpers.WriteJsonAsync(context.Response, 200, response);
        }
        catch (PostgresException pgEx)
        {
            await ResponseHelpers.WriteErrorAsync(context.Response, 400, pgEx.MessageText);
        }
        catch
        {
            await ResponseHelpers.WriteErrorAsync(context.Response, 500, "failed to retry task");
        }
    }

    // =========================================================================
    // /api/events
    // =========================================================================

    private async Task HandleEventsAsync(HttpContext context)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        var ct = cts.Token;

        var limit = QueueHelpers.ParsePositiveInt(context.Request.Query["limit"], 100);
        if (limit > 1000) limit = 1000;

        var queueFilter = context.Request.Query["queue"].ToString().Trim();
        var eventFilter = context.Request.Query["eventName"].ToString().Trim();
        var afterTime = QueueHelpers.ParseOptionalTime(context.Request.Query["after"].ToString().Trim());
        var beforeTime = QueueHelpers.ParseOptionalTime(context.Request.Query["before"].ToString().Trim());

        var events = new List<QueueEvent>();
        try
        {
            if (!string.IsNullOrEmpty(queueFilter))
            {
                events = await FetchQueueEventsAsync(queueFilter, limit, eventFilter, afterTime, beforeTime, ct);
            }
            else
            {
                var queueNames = await ListQueueNamesAsync(ct);
                foreach (var queueName in queueNames)
                {
                    try
                    {
                        var qe = await FetchQueueEventsAsync(queueName, limit, eventFilter, afterTime, beforeTime, ct);
                        events.AddRange(qe);
                    }
                    catch { /* skip */ }
                }

                events.Sort((a, b) =>
                {
                    var ta = a.EmittedAt ?? a.CreatedAt;
                    var tb = b.EmittedAt ?? b.CreatedAt;
                    return tb.CompareTo(ta);
                });

                if (events.Count > limit)
                    events = events[..limit];
            }
        }
        catch
        {
            await ResponseHelpers.WriteErrorAsync(context.Response, 500, "failed to query events");
            return;
        }

        await ResponseHelpers.WriteJsonAsync(context.Response, 200, events);
    }

    private async Task<List<QueueEvent>> FetchQueueEventsAsync(
        string queueName,
        int limit,
        string eventName,
        DateTime? afterTime,
        DateTime? beforeTime,
        CancellationToken ct)
    {
        if (!await EnsureQueueExistsAsync(queueName, ct))
            return [];

        var etable = QueueHelpers.QueueTableIdentifier("e", queueName);
        var clauses = new List<string> { "payload IS NOT NULL" };
        var paramValues = new List<object?>();

        if (!string.IsNullOrEmpty(eventName))
        {
            paramValues.Add(eventName);
            clauses.Add($"event_name = ${paramValues.Count}");
        }
        if (afterTime.HasValue)
        {
            paramValues.Add(afterTime.Value);
            clauses.Add($"emitted_at >= ${paramValues.Count}");
        }
        if (beforeTime.HasValue)
        {
            paramValues.Add(beforeTime.Value);
            clauses.Add($"emitted_at <= ${paramValues.Count}");
        }

        paramValues.Add(limit);
        var limitPos = paramValues.Count;

        var whereClause = clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : "";

        var sql = $"""
            SELECT event_name, payload, emitted_at, emitted_at AS created_at
            FROM absurd.{etable}
            {whereClause}
            ORDER BY emitted_at DESC
            LIMIT ${limitPos}
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var pv in paramValues)
            cmd.Parameters.AddWithValue(pv ?? DBNull.Value);

        var events = new List<QueueEvent>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            events.Add(new QueueEvent
            {
                QueueName = queueName,
                EventName = reader.GetString(0),
                Payload = QueueHelpers.ParseJsonElement(reader.IsDBNull(1) ? null : reader.GetString(1)),
                EmittedAt = reader.IsDBNull(2) ? null : reader.GetDateTime(2).ToUniversalTime(),
                CreatedAt = reader.IsDBNull(3) ? DateTime.UtcNow : reader.GetDateTime(3).ToUniversalTime(),
            });
        }

        return events;
    }

    // =========================================================================
    // Shared DB helpers
    // =========================================================================

    private async Task<List<string>> ListQueueNamesAsync(CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT queue_name FROM absurd.queues ORDER BY queue_name";
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var names = new List<string>();
        while (await reader.ReadAsync(ct))
        {
            var name = reader.GetString(0);
            if (!string.IsNullOrEmpty(name))
                names.Add(name);
        }

        return names;
    }

    private async Task<bool> EnsureQueueExistsAsync(string queueName, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT queue_name FROM absurd.queues WHERE queue_name = $1";
        cmd.Parameters.AddWithValue(queueName);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null && result != DBNull.Value;
    }

    private async Task<string?> FindQueueForRunAsync(Guid runId, CancellationToken ct)
    {
        var queueNames = await ListQueueNamesAsync(ct);
        foreach (var queueName in queueNames)
        {
            var rtable = QueueHelpers.QueueTableIdentifier("r", queueName);
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT 1 FROM absurd.{rtable} WHERE run_id = $1 LIMIT 1";
            cmd.Parameters.AddWithValue(runId);
            var result = await cmd.ExecuteScalarAsync(ct);
            if (result != null && result != DBNull.Value)
                return queueName;
        }

        return null;
    }

    private async Task<int> GetTaskAttemptsAsync(string queueName, Guid taskId, CancellationToken ct)
    {
        var ttable = QueueHelpers.QueueTableIdentifier("t", queueName);
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT attempts FROM absurd.{ttable} WHERE task_id = $1 LIMIT 1";
        cmd.Parameters.AddWithValue(taskId);
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result == null || result == DBNull.Value)
            throw new InvalidOperationException("task not found");

        return Convert.ToInt32(result);
    }

    // =========================================================================
    // Reader helpers
    // =========================================================================

    private async Task<List<TaskSummary>> QueryTaskSummariesAsync(string sql, bool includeParams, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var tasks = new List<TaskSummary>();
        while (await reader.ReadAsync(ct))
            tasks.Add(ReadTaskSummary(reader, includeParams));

        return tasks;
    }

    private static TaskSummary ReadTaskSummary(NpgsqlDataReader reader, bool hasParams)
    {
        // Columns: task_id, run_id, queue_name, task_name, state/status, attempt, max_attempts,
        //          created_at, updated_at, completed_at, claimed_by[, params]
        return new TaskSummary
        {
            TaskId = reader.GetGuid(0),
            RunId = reader.GetGuid(1),
            QueueName = reader.GetString(2),
            TaskName = reader.GetString(3),
            Status = reader.GetString(4),
            Attempt = reader.GetInt32(5),
            MaxAttempts = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6),
            CreatedAt = reader.GetDateTime(7).ToUniversalTime(),
            UpdatedAt = reader.GetDateTime(8).ToUniversalTime(),
            CompletedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9).ToUniversalTime(),
            WorkerId = reader.IsDBNull(10) ? null : reader.GetString(10),
            Params = hasParams && !reader.IsDBNull(11)
                ? QueueHelpers.ParseJsonElement(reader.GetString(11))
                : null,
        };
    }

    private static TaskDetail ReadTaskDetail(NpgsqlDataReader reader)
    {
        // Columns: task_id, run_id, queue_name, task_name, t.state, attempt, max_attempts,
        //          params, retry_strategy, headers, state_detail,
        //          created_at, updated_at, completed_at, claimed_by
        return new TaskDetail
        {
            TaskId = reader.GetGuid(0),
            RunId = reader.GetGuid(1),
            QueueName = reader.GetString(2),
            TaskName = reader.GetString(3),
            Status = reader.GetString(4),
            Attempt = reader.GetInt32(5),
            MaxAttempts = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6),
            Params = QueueHelpers.ParseJsonElement(reader.IsDBNull(7) ? null : reader.GetString(7)),
            RetryStrategy = QueueHelpers.ParseJsonElement(reader.IsDBNull(8) ? null : reader.GetString(8)),
            Headers = QueueHelpers.ParseJsonElement(reader.IsDBNull(9) ? null : reader.GetString(9)),
            State = QueueHelpers.ParseJsonElement(reader.IsDBNull(10) ? null : reader.GetString(10)),
            CreatedAt = reader.GetDateTime(11).ToUniversalTime(),
            UpdatedAt = reader.GetDateTime(12).ToUniversalTime(),
            CompletedAt = reader.IsDBNull(13) ? null : reader.GetDateTime(13).ToUniversalTime(),
            WorkerId = reader.IsDBNull(14) ? null : reader.GetString(14),
        };
    }

    // =========================================================================
    // Misc helpers
    // =========================================================================

    private static TaskListResponse EmptyTaskListResponse(int page, int perPage, List<string> queueNames) =>
        new()
        {
            Items = [],
            Total = 0,
            HasMore = false,
            Page = page,
            PerPage = perPage,
            AvailableStatuses = [.. QueueHelpers.AllTaskStatuses()],
            AvailableQueues = queueNames,
            AvailableTaskNames = [],
        };

}
