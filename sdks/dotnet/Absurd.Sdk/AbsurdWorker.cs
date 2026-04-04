using Absurd.Options;
using Microsoft.Extensions.Logging;

namespace Absurd;

/// <summary>
/// A long-lived background worker that polls for and executes tasks from the queue.
/// Obtain an instance via <see cref="AbsurdClient.StartWorkerAsync"/>.
/// </summary>
public sealed class AbsurdWorker : IAsyncDisposable
{
    private readonly AbsurdClient _client;
    private readonly WorkerOptions _options;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;

    internal AbsurdWorker(AbsurdClient client, WorkerOptions options)
    {
        _client = client;
        _options = options;
    }

    internal void Start()
    {
        _loopTask = RunAsync(_cts.Token);
    }

    /// <summary>
    /// Signals the worker to stop accepting new tasks and waits for all in-flight
    /// tasks to complete (or until <paramref name="ct"/> fires).
    /// </summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        _cts.Cancel();
        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                // The caller's token fired — return without waiting further.
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts.Dispose();
    }

    // -------------------------------------------------------------------------
    // Poll loop
    // -------------------------------------------------------------------------

    private async Task RunAsync(CancellationToken ct)
    {
        var concurrency = Math.Max(_options.Concurrency, 1);
        var effectiveBatch = _options.BatchSize ?? concurrency;
        var workerId = _options.WorkerId ?? $"{Environment.MachineName}:{Environment.ProcessId}";
        var interval = TimeSpan.FromSeconds(Math.Max(_options.PollIntervalSeconds, 0.01));

        using var gate = new SemaphoreSlim(concurrency, concurrency);
        using var timer = new PeriodicTimer(interval);
        var pending = new List<Task>();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Remove completed tasks so the list doesn't grow without bound.
                pending.RemoveAll(t => t.IsCompleted);

                try
                {
                    await timer.WaitForNextTickAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var available = gate.CurrentCount;
                if (available == 0)
                    continue;

                var toClaim = Math.Min(effectiveBatch, available);

                IReadOnlyList<Internal.ClaimedTask> claimed;
                try
                {
                    claimed = await _client.ClaimTasksAsync(workerId, _options.ClaimTimeoutSeconds, toClaim, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _client.Log.LogError(ex, "[absurd] Error claiming tasks");
                    if (_options.OnError is not null)
                        try { await _options.OnError(ex); } catch { /* swallow */ }
                    continue;
                }

                foreach (var task in claimed)
                {
                    // Non-blocking acquire: we claimed exactly `toClaim` tasks and read
                    // `gate.CurrentCount` before claiming, so slots are available.
                    if (!await gate.WaitAsync(0))
                        break; // Defensive: no slot — skip (shouldn't normally happen).

                    var t = _client
                        .ExecuteClaimedTaskAsync(
                            task,
                            _options.ClaimTimeoutSeconds,
                            _options.FatalOnLeaseTimeout,
                            _options.OnError,
                            CancellationToken.None) // Don't cancel running tasks on shutdown.
                        .ContinueWith(_ => gate.Release(), TaskScheduler.Default);
                    pending.Add(t);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown path.
        }
        finally
        {
            // Wait for all in-flight tasks to finish before returning.
            if (pending.Count > 0)
                await Task.WhenAll(pending);
        }
    }
}
