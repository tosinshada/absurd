using System.Text.Json;

namespace Absurd.Options;

/// <summary>
/// Options controlling how a task is spawned.
/// </summary>
public sealed class SpawnOptions
{
    /// <summary>Maximum number of retry attempts.</summary>
    public int? MaxAttempts { get; set; }

    /// <summary>Backoff strategy for retries.</summary>
    public RetryStrategy? RetryStrategy { get; set; }

    /// <summary>Arbitrary metadata attached to the task.</summary>
    public Dictionary<string, JsonElement>? Headers { get; set; }

    /// <summary>Target queue. Overrides the client default.</summary>
    public string? Queue { get; set; }

    /// <summary>Auto-cancellation policy for the task.</summary>
    public CancellationPolicy? Cancellation { get; set; }

    /// <summary>
    /// Deduplication key. If a task with the same key already exists,
    /// that task is returned with <see cref="SpawnResult.Created"/> set to <c>false</c>.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}
