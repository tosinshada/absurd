namespace Absurd;

/// <summary>
/// Result returned by <see cref="AbsurdClient.SpawnAsync{TParams}"/>.
/// </summary>
public sealed class SpawnResult
{
    /// <summary>Unique task identifier (UUIDv7).</summary>
    public required string TaskId { get; init; }

    /// <summary>Current run identifier.</summary>
    public required string RunId { get; init; }

    /// <summary>Attempt number (1-based).</summary>
    public required int Attempt { get; init; }

    /// <summary>
    /// <c>true</c> if this is a newly created task; <c>false</c> if an existing
    /// task was returned due to an idempotency key match.
    /// </summary>
    public required bool Created { get; init; }
}
