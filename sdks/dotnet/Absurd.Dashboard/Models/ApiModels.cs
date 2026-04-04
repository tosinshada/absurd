using System.Text.Json;
using System.Text.Json.Serialization;

namespace Absurd.Dashboard.Models;

/// <summary>
/// Summary of a single task run — used in list views.
/// Matches Go's <c>TaskSummary</c> struct.
/// </summary>
public sealed class TaskSummary
{
    [JsonPropertyName("taskId")] public Guid TaskId { get; init; }
    [JsonPropertyName("runId")] public Guid RunId { get; init; }
    [JsonPropertyName("queueName")] public string QueueName { get; init; } = "";
    [JsonPropertyName("taskName")] public string TaskName { get; init; } = "";
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("attempt")] public int Attempt { get; init; }
    [JsonPropertyName("maxAttempts"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxAttempts { get; init; }
    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; init; }
    [JsonPropertyName("updatedAt")] public DateTime UpdatedAt { get; init; }
    [JsonPropertyName("completedAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? CompletedAt { get; init; }
    [JsonPropertyName("workerId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkerId { get; init; }
    [JsonPropertyName("params"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Params { get; init; }
}

/// <summary>
/// Full task detail including checkpoints, wait states, and run result.
/// Matches Go's <c>TaskDetail</c> struct.
/// </summary>
public sealed record TaskDetail
{
    [JsonPropertyName("taskId")] public Guid TaskId { get; init; }
    [JsonPropertyName("runId")] public Guid RunId { get; init; }
    [JsonPropertyName("queueName")] public string QueueName { get; init; } = "";
    [JsonPropertyName("taskName")] public string TaskName { get; init; } = "";
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("attempt")] public int Attempt { get; init; }
    [JsonPropertyName("maxAttempts"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxAttempts { get; init; }
    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; init; }
    [JsonPropertyName("updatedAt")] public DateTime UpdatedAt { get; init; }
    [JsonPropertyName("completedAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? CompletedAt { get; init; }
    [JsonPropertyName("workerId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkerId { get; init; }
    [JsonPropertyName("params"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Params { get; init; }
    [JsonPropertyName("retryStrategy"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? RetryStrategy { get; init; }
    [JsonPropertyName("headers"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Headers { get; init; }
    [JsonPropertyName("state"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? State { get; init; }
    [JsonPropertyName("checkpoints")] public List<CheckpointState> Checkpoints { get; init; } = [];
    [JsonPropertyName("waits")] public List<WaitState> Waits { get; init; } = [];
}

/// <summary>Matches Go's <c>CheckpointState</c>.</summary>
public sealed class CheckpointState
{
    [JsonPropertyName("stepName")] public string StepName { get; init; } = "";
    [JsonPropertyName("state"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? State { get; init; }
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("ownerRunId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? OwnerRunId { get; init; }
    [JsonPropertyName("expiresAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? ExpiresAt { get; init; }
    [JsonPropertyName("updatedAt")] public DateTime UpdatedAt { get; init; }
}

/// <summary>Matches Go's <c>WaitState</c>.</summary>
public sealed class WaitState
{
    [JsonPropertyName("waitType")] public string WaitType { get; init; } = "";
    [JsonPropertyName("wakeAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? WakeAt { get; init; }
    [JsonPropertyName("wakeEvent"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WakeEvent { get; init; }
    [JsonPropertyName("stepName"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StepName { get; init; }
    [JsonPropertyName("payload"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Payload { get; init; }
    [JsonPropertyName("eventPayload"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? EventPayload { get; init; }
    [JsonPropertyName("emittedAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? EmittedAt { get; init; }
    [JsonPropertyName("updatedAt")] public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// Paginated task list response. Matches Go's <c>TaskListResponse</c>.
/// </summary>
public sealed class TaskListResponse
{
    [JsonPropertyName("items")] public List<TaskSummary> Items { get; init; } = [];
    [JsonPropertyName("total")] public int Total { get; init; }
    [JsonPropertyName("hasMore")] public bool HasMore { get; init; }
    [JsonPropertyName("page")] public int Page { get; init; }
    [JsonPropertyName("perPage")] public int PerPage { get; init; }
    [JsonPropertyName("availableStatuses")] public List<string> AvailableStatuses { get; init; } = [];
    [JsonPropertyName("availableQueues")] public List<string> AvailableQueues { get; init; } = [];
    [JsonPropertyName("availableTaskNames")] public List<string> AvailableTaskNames { get; init; } = [];
}

/// <summary>
/// Queue summary with per-state task counts. Matches Go's <c>QueueSummary</c>.
/// </summary>
public sealed class QueueSummary
{
    [JsonPropertyName("queueName")] public string QueueName { get; init; } = "";
    [JsonPropertyName("createdAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? CreatedAt { get; init; }
    [JsonPropertyName("pendingCount")] public long PendingCount { get; init; }
    [JsonPropertyName("runningCount")] public long RunningCount { get; init; }
    [JsonPropertyName("sleepingCount")] public long SleepingCount { get; init; }
    [JsonPropertyName("completedCount")] public long CompletedCount { get; init; }
    [JsonPropertyName("failedCount")] public long FailedCount { get; init; }
    [JsonPropertyName("cancelledCount")] public long CancelledCount { get; init; }
}

/// <summary>
/// Per-queue metrics scraped at a point in time. Matches Go's <c>QueueMetrics</c>.
/// </summary>
public sealed class QueueMetrics
{
    [JsonPropertyName("queueName")] public string QueueName { get; init; } = "";
    [JsonPropertyName("queueLength")] public long QueueLength { get; init; }
    [JsonPropertyName("queueVisibleLength")] public long QueueVisibleLength { get; init; }
    [JsonPropertyName("totalMessages")] public long TotalMessages { get; init; }
    [JsonPropertyName("newestMsgAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? NewestMsgAt { get; init; }
    [JsonPropertyName("oldestMsgAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? OldestMsgAt { get; init; }
    [JsonPropertyName("scrapeTime")] public DateTime ScrapeTime { get; init; }
}

/// <summary>
/// Event from a queue's event table. Matches Go's <c>QueueEvent</c>.
/// </summary>
public sealed class QueueEvent
{
    [JsonPropertyName("queueName")] public string QueueName { get; init; } = "";
    [JsonPropertyName("eventName")] public string EventName { get; init; } = "";
    [JsonPropertyName("payload"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Payload { get; init; }
    [JsonPropertyName("emittedAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? EmittedAt { get; init; }
    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Request body for POST /api/tasks/retry. Matches Go's <c>retryTaskRequest</c>.
/// </summary>
public sealed class RetryTaskRequest
{
    [JsonPropertyName("taskId")] public Guid TaskId { get; init; }
    [JsonPropertyName("queueName")] public string QueueName { get; init; } = "";
    [JsonPropertyName("spawnNewTask")] public bool SpawnNewTask { get; init; }
    [JsonPropertyName("maxAttempts")] public int? MaxAttempts { get; init; }
    [JsonPropertyName("extraAttempts")] public int? ExtraAttempts { get; init; }
}

/// <summary>
/// Response for POST /api/tasks/retry. Matches Go's <c>retryTaskResponse</c>.
/// </summary>
public sealed class RetryTaskResponse
{
    [JsonPropertyName("taskId")] public Guid TaskId { get; init; }
    [JsonPropertyName("runId")] public Guid RunId { get; init; }
    [JsonPropertyName("attempt")] public int Attempt { get; init; }
    [JsonPropertyName("created")] public bool Created { get; init; }
    [JsonPropertyName("queueName")] public string QueueName { get; init; } = "";
}
