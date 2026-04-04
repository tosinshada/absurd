/// Spawn a task and await its completion.
///
/// Usage:
///   dotnet run --project examples/SpawnAndAwait -- "Host=localhost;Database=mydb"
using Absurd;

var connectionString = args.Length > 0 ? args[0] : "postgresql://localhost/absurd";

await using var app = new AbsurdClient(new AbsurdOptions
{
    ConnectionString = connectionString,
    QueueName        = "default",
});

// Ensure the queue exists.
await app.CreateQueueAsync("default");

// Register the task handler.
app.RegisterTask<OrderParams>("process-order", async (order, ctx) =>
{
    var payment = await ctx.StepAsync("process-payment", async () =>
    {
        // Simulate payment processing.
        await Task.Delay(100);
        return new { PaymentId = $"pay-{order.OrderId}", Amount = order.Amount };
    });

    var receipt = await ctx.StepAsync("send-receipt", async () =>
    {
        await Task.Delay(50);
        return new { SentTo = order.Email, PaymentRef = payment.PaymentId };
    });

    return new { order.OrderId, payment, receipt };
});

// Spawn.
var result = await app.SpawnAsync("process-order", new OrderParams
{
    OrderId = "ORD-001",
    Amount  = 99.99m,
    Email   = "customer@example.com",
});

Console.WriteLine($"Spawned task {result.TaskId} (created={result.Created})");

// Run a single batch so the task executes.
await app.WorkBatchAsync(claimTimeoutSeconds: 30, batchSize: 1);

// Poll until terminal.
var final = await app.AwaitTaskResultAsync(result.TaskId, timeoutSeconds: 30);
Console.WriteLine($"State: {final.State}");
Console.WriteLine($"Result: {final.Result}");

record OrderParams(string OrderId, decimal Amount, string Email);
