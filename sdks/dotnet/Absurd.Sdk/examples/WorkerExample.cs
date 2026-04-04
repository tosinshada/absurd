/// Long-lived worker example.
///
/// Registers a task, starts a worker, and waits for Ctrl+C.
///
/// Usage:
///   dotnet run --project examples/WorkerExample -- "Host=localhost;Database=mydb"
using Absurd;

var connectionString = args.Length > 0 ? args[0] : "postgresql://localhost/absurd";

await using var app = new AbsurdClient(new AbsurdOptions
{
    ConnectionString = connectionString,
    QueueName        = "jobs",
});

await app.CreateQueueAsync("jobs");

app.RegisterTask<JobParams>("run-job", async (job, ctx) =>
{
    var result = await ctx.StepAsync("execute", async () =>
    {
        Console.WriteLine($"[{job.JobId}] running...");
        await Task.Delay(200);
        return new { Done = true, JobId = job.JobId };
    });

    return result;
});

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

Console.WriteLine("Worker started. Press Ctrl+C to stop.");
var worker = await app.StartWorkerAsync(new WorkerOptions
{
    Concurrency         = 4,
    ClaimTimeoutSeconds = 60,
    FatalOnLeaseTimeout = true,
    OnError             = ex =>
    {
        Console.Error.WriteLine($"Task error: {ex.Message}");
        return Task.CompletedTask;
    },
});

await cts.Token.WhenAsync();
Console.WriteLine("Shutting down…");
await worker.StopAsync();
Console.WriteLine("Worker stopped.");

record JobParams(string JobId);

static class CancellationTokenExtensions
{
    public static Task WhenAsync(this CancellationToken ct)
    {
        var tcs = new TaskCompletionSource();
        ct.Register(() => tcs.TrySetResult());
        return tcs.Task;
    }
}
