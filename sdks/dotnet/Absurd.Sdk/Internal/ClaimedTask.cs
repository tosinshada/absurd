using System.Text.Json;

namespace Absurd.Internal;

/// <summary>Represents a task claimed from the queue by a worker.</summary>
internal sealed class ClaimedTask
{
    public required string RunId { get; init; }
    public required string TaskId { get; init; }
    public required string TaskName { get; init; }
    public required int Attempt { get; init; }
    public JsonElement Params { get; init; }
    public JsonElement? RetryStrategy { get; init; }
    public int? MaxAttempts { get; init; }
    public JsonElement? Headers { get; init; }
    public string? WakeEvent { get; init; }
    public JsonElement? EventPayload { get; init; }
}
