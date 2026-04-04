/// Sleep and events example.
///
/// Shows SleepForAsync (timed suspension) and AwaitEventAsync (event-driven
/// suspension) in a single durable workflow.
///
/// Usage:
///   dotnet run --project examples/SleepAndEvents -- "Host=localhost;Database=mydb"
using System.Text.Json;
using Absurd;

var connectionString = args.Length > 0 ? args[0] : "postgresql://localhost/absurd";

await using var app = new AbsurdClient(new AbsurdOptions
{
    ConnectionString = connectionString,
    QueueName        = "fulfilment",
});

await app.CreateQueueAsync("fulfilment");

// Register a workflow that sleeps, then waits for an event.
app.RegisterTask<OrderParams>("fulfil-order", async (order, ctx) =>
{
    // Step 1: process payment.
    var payment = await ctx.StepAsync("payment", async () =>
    {
        await Task.Delay(50);
        return new { PaymentId = $"pay-{order.OrderId}" };
    });

    // Step 2: wait 1 second for the warehouse to prepare the shipment.
    await ctx.SleepForAsync("wait-for-packing", TimeSpan.FromSeconds(1));

    // Step 3: wait for external event (shipment picked up).
    var shipment = await ctx.AwaitEventAsync(
        $"shipment.dispatched:{order.OrderId}",
        timeoutSeconds: 30);

    // Step 4: send confirmation.
    var confirmation = await ctx.StepAsync("send-confirmation", async () =>
    {
        Console.WriteLine($"Order {order.OrderId} dispatched! {shipment}");
        return new { Sent = true };
    });

    return new { order.OrderId, payment, confirmation };
});

// Spawn the task.
var spawn = await app.SpawnAsync("fulfil-order", new OrderParams("ORD-999"));
Console.WriteLine($"Spawned task {spawn.TaskId}");

// Run batch 1: task runs steps 1 & 2's setup, then suspends on sleep.
await app.WorkBatchAsync(claimTimeoutSeconds: 30, batchSize: 1);
Console.WriteLine("Task sleeping…");

// Wait for the sleep to elapse.
await Task.Delay(1200);

// Run batch 2: task wakes from sleep, suspends on await-event.
await app.WorkBatchAsync(claimTimeoutSeconds: 30, batchSize: 1);
Console.WriteLine("Task waiting for shipment event…");

// Emit the external event.
await app.EmitEventAsync(
    $"shipment.dispatched:ORD-999",
    new { TrackingNumber = "TRACK-42" });

// Run batch 3: task wakes from event, completes.
await app.WorkBatchAsync(claimTimeoutSeconds: 30, batchSize: 1);

var result = await app.AwaitTaskResultAsync(spawn.TaskId, timeoutSeconds: 10);
Console.WriteLine($"State:  {result.State}");
Console.WriteLine($"Result: {result.Result}");

record OrderParams(string OrderId);
