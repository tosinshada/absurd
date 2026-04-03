namespace Absurd;

/// <summary>
/// Options for registering a task handler with <see cref="AbsurdClient.RegisterTask{TParams}"/>.
/// </summary>
public sealed class TaskRegistrationOptions
{
    /// <summary>Task name. Must match the name used when spawning.</summary>
    public required string Name { get; set; }

    /// <summary>Queue this task belongs to. Defaults to the client queue.</summary>
    public string? Queue { get; set; }

    /// <summary>Default maximum attempts for this task. Overrides the client default.</summary>
    public int? DefaultMaxAttempts { get; set; }

    /// <summary>Default auto-cancellation policy for this task.</summary>
    public CancellationPolicy? DefaultCancellation { get; set; }
}
