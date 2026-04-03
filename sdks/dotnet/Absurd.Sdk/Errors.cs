namespace Absurd;

/// <summary>
/// Thrown by <see cref="AbsurdClient.AwaitTaskResultAsync"/> and
/// <see cref="TaskContext.AwaitEventAsync"/> when the configured timeout
/// elapses before the awaited condition is met.
/// </summary>
public sealed class AbsurdTimeoutException : Exception
{
    /// <inheritdoc/>
    public AbsurdTimeoutException(string message) : base(message) { }
}

/// <summary>
/// Internal exception thrown when a task run detects that the task has been cancelled.
/// Never visible to user task handler code — the SDK catches it and marks the run cancelled.
/// </summary>
internal sealed class TaskCancelledException : Exception
{
    public TaskCancelledException() : base("Task was cancelled.") { }
}

/// <summary>
/// Internal exception thrown when the current run has already failed
/// (e.g. the claim lease expired) and can no longer make progress.
/// Never visible to user task handler code.
/// </summary>
internal sealed class FailedTaskException : Exception
{
    public FailedTaskException() : base("Task run has already failed.") { }
}

/// <summary>
/// Internal exception thrown to suspend the current run (sleep or event await).
/// Never visible to user task handler code — the SDK catches it and reschedules.
/// </summary>
internal sealed class SuspendTaskException : Exception
{
    public SuspendTaskException() : base("Task suspended.") { }
}
