namespace Absurd.Options;

/// <summary>
/// Defines automatic cancellation conditions for a task.
/// </summary>
public sealed class CancellationPolicy
{
    /// <summary>
    /// Cancel the task after this many seconds of total lifetime, regardless of state.
    /// </summary>
    public double? MaxDuration { get; set; }

    /// <summary>
    /// Cancel the task if no checkpoint has been written for this many seconds.
    /// </summary>
    public double? MaxDelay { get; set; }
}
