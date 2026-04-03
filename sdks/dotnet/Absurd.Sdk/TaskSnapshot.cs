using System.Text.Json;

namespace Absurd;

/// <summary>
/// The current state of a task, as returned by
/// <see cref="AbsurdClient.FetchTaskResultAsync"/> and
/// <see cref="AbsurdClient.AwaitTaskResultAsync"/>.
/// </summary>
/// <remarks>
/// Mirrors the TypeScript SDK's <c>TaskResultSnapshot</c> discriminated union.
/// Inspect <see cref="State"/> to determine which properties are populated.
/// </remarks>
public sealed class TaskSnapshot
{
    /// <summary>Task state. One of: <c>pending</c>, <c>running</c>, <c>sleeping</c>, <c>completed</c>, <c>failed</c>, <c>cancelled</c>.</summary>
    public required string State { get; init; }

    /// <summary>
    /// The task's return value, serialised as a JSON element.
    /// Only populated when <see cref="State"/> is <c>"completed"</c>.
    /// </summary>
    public JsonElement? Result { get; init; }

    /// <summary>
    /// The failure reason, serialised as a JSON element.
    /// Only populated when <see cref="State"/> is <c>"failed"</c>.
    /// </summary>
    public JsonElement? Failure { get; init; }

    /// <summary>Returns <c>true</c> when the task has reached a terminal state.</summary>
    public bool IsTerminal =>
        State is "completed" or "failed" or "cancelled";
}
