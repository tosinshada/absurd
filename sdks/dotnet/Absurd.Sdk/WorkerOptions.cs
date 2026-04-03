namespace Absurd;

/// <summary>
/// Options for <see cref="AbsurdClient.StartWorkerAsync"/>.
/// </summary>
public sealed class WorkerOptions
{
    /// <summary>Number of tasks that may run concurrently. Defaults to <c>1</c>.</summary>
    public int Concurrency { get; set; } = 1;

    /// <summary>
    /// Lease duration in seconds per claimed task. Defaults to <c>120</c>.
    /// </summary>
    public int ClaimTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Number of tasks to claim per poll cycle. Defaults to <see cref="Concurrency"/>.
    /// </summary>
    public int? BatchSize { get; set; }

    /// <summary>
    /// Seconds between idle polls. Defaults to <c>0.25</c>.
    /// </summary>
    public double PollIntervalSeconds { get; set; } = 0.25;

    /// <summary>
    /// Identifier reported to Postgres when claiming tasks.
    /// Defaults to <c>"hostname:pid"</c>.
    /// </summary>
    public string? WorkerId { get; set; }

    /// <summary>
    /// When <c>true</c> (the default), the process exits with a non-zero exit code if a task
    /// holds its claim for more than <c>2 × ClaimTimeoutSeconds</c>.
    /// </summary>
    public bool FatalOnLeaseTimeout { get; set; } = true;

    /// <summary>
    /// Optional callback invoked when a task handler throws an unhandled exception.
    /// </summary>
    public Func<Exception, Task>? OnError { get; set; }
}
