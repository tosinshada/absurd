namespace Absurd.Options;

/// <summary>
/// Options for <see cref="AbsurdClient.RetryTaskAsync"/>.
/// </summary>
public sealed class RetryTaskOptions
{
    /// <summary>
    /// Maximum number of attempts. For in-place retry, must exceed the current attempt count;
    /// defaults to <c>current_attempts + 1</c>. For <see cref="SpawnNewTask"/>, overrides the
    /// copied value on the new task.
    /// </summary>
    public int? MaxAttempts { get; set; }

    /// <summary>
    /// When <c>true</c>, spawns a brand-new task from the original inputs rather than
    /// re-queuing the existing task record. Defaults to <c>false</c> (in-place retry).
    /// </summary>
    public bool SpawnNewTask { get; set; }
}
