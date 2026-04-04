/// Hooks example: inject a trace ID into every spawned task.
///
/// Usage:
///   dotnet run --project examples/HooksExample -- "Host=localhost;Database=mydb"
using System.Text.Json;
using Absurd;

var connectionString = args.Length > 0 ? args[0] : "postgresql://localhost/absurd";

await using var app = new AbsurdClient(new AbsurdOptions
{
    ConnectionString = connectionString,
    QueueName        = "traced",
    Hooks            = new TracingHooks(),
});

await app.CreateQueueAsync("traced");

app.RegisterTask<object>("traced-task", async (_, ctx) =>
{
    // The traceId header is injected by the BeforeSpawnAsync hook.
    var traceId = ctx.Headers.TryGetValue("traceId", out var v) ? v.GetString() : "(none)";
    Console.WriteLine($"Running with traceId={traceId}");

    await ctx.StepAsync("work", async () =>
    {
        await Task.Delay(50);
        return new { done = true };
    });
});

// Current trace simulation
var currentTraceId = Guid.NewGuid().ToString("N")[..12];

var spawn = await app.SpawnAsync<object>("traced-task", new { });
Console.WriteLine($"Spawned {spawn.TaskId}");

await app.WorkBatchAsync(claimTimeoutSeconds: 30, batchSize: 1);

var result = await app.AwaitTaskResultAsync(spawn.TaskId, timeoutSeconds: 10);
Console.WriteLine($"State: {result.State}");

// ─── Hook implementation ──────────────────────────────────────────────────────

sealed class TracingHooks : IAbsurdHooks
{
    // In a real app this would come from Activity.Current, AsyncLocal, etc.
    private static string GetCurrentTraceId() => Guid.NewGuid().ToString("N")[..12];

    public Task<SpawnOptions> BeforeSpawnAsync(
        string taskName, JsonElement? parameters, SpawnOptions options)
    {
        var headers = options.Headers is not null
            ? new Dictionary<string, JsonElement>(options.Headers)
            : new Dictionary<string, JsonElement>();

        headers["traceId"] = JsonSerializer.SerializeToElement(GetCurrentTraceId());

        return Task.FromResult(new SpawnOptions
        {
            MaxAttempts    = options.MaxAttempts,
            RetryStrategy  = options.RetryStrategy,
            Headers        = headers,
            Queue          = options.Queue,
            Cancellation   = options.Cancellation,
            IdempotencyKey = options.IdempotencyKey,
        });
    }

    public async Task WrapTaskExecutionAsync(TaskContext ctx, Func<Task> execute)
    {
        var traceId = ctx.Headers.TryGetValue("traceId", out var v) ? v.GetString() : null;
        Console.WriteLine($"[WrapTaskExecution] traceId={traceId}");
        await execute();
    }
}
