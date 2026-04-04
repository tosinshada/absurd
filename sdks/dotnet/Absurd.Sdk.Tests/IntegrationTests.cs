using System.Text.Json;
using Xunit;

namespace Absurd.Tests;

// ─── Fixture wiring ───────────────────────────────────────────────────────────

[CollectionDefinition("Integration")]
public sealed class IntegrationCollection : ICollectionFixture<TestFixture> { }

// ─── Hook helpers used by tests ───────────────────────────────────────────────

/// <summary>Injects a traceId header before every spawn.</summary>
internal sealed class InjectingHooks(Func<string> traceIdFactory) : IAbsurdHooks
{
    public Task<SpawnOptions> BeforeSpawnAsync(string _, JsonElement? __, SpawnOptions options)
    {
        var headers = options.Headers is not null
            ? new Dictionary<string, JsonElement>(options.Headers)
            : new Dictionary<string, JsonElement>();

        headers["traceId"] = JsonSerializer.SerializeToElement(traceIdFactory());

        return Task.FromResult(new SpawnOptions
        {
            MaxAttempts   = options.MaxAttempts,
            RetryStrategy = options.RetryStrategy,
            Headers       = headers,
            Queue         = options.Queue,
            Cancellation  = options.Cancellation,
            IdempotencyKey = options.IdempotencyKey,
        });
    }
}

/// <summary>Calls an action then delegates to the task handler.</summary>
internal sealed class WrappingHooks(Action onWrap) : IAbsurdHooks
{
    public Task WrapTaskExecutionAsync(TaskContext ctx, Func<Task> execute)
    {
        onWrap();
        return execute();
    }
}

// ─── Tests ────────────────────────────────────────────────────────────────────

[Collection("Integration")]
public sealed class IntegrationTests(TestFixture fixture)
{
    /// Returns a unique queue name for test isolation.
    private static string Q() => "t" + Guid.NewGuid().ToString("N")[..16];

    /// Runs WorkBatch(batchSize=1) repeatedly until the task reaches a terminal
    /// state, then returns the final snapshot.
    private async Task<TaskSnapshot> RunUntilDoneAsync(
        AbsurdClient client,
        string taskId,
        int maxBatches = 10)
    {
        for (var i = 0; i < maxBatches; i++)
        {
            await client.WorkBatchAsync(claimTimeoutSeconds: 30, batchSize: 1);
            var snapshot = await client.FetchTaskResultAsync(taskId);
            if (snapshot?.IsTerminal == true)
                return snapshot;
        }

        throw new TimeoutException(
            $"Task {taskId} did not reach a terminal state after {maxBatches} WorkBatch calls.");
    }

    // ── 8.2 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SpawnAndAwait_SimpleTaskWithOneStep_Completes()
    {
        var queue = Q();
        await using var client = fixture.CreateClient(queue);
        await client.CreateQueueAsync(queue);

        client.RegisterTask<object>("simple", async (_, ctx) =>
            await ctx.StepAsync("echo", () => Task.FromResult<object>(new { ok = true })));

        var spawn = await client.SpawnAsync<object>("simple", new { });

        Assert.True(spawn.Created);
        Assert.Equal(1, spawn.Attempt);

        var result = await RunUntilDoneAsync(client, spawn.TaskId);
        Assert.Equal("completed", result.State);
    }

    // ── 8.3 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StepCaching_FnNotCalledOnRetry()
    {
        var queue = Q();
        await using var client = fixture.CreateClient(queue);
        await client.CreateQueueAsync(queue);

        var stepInvocations    = 0;
        var handlerInvocations = 0;

        client.RegisterTask<object>("cached", async (_, ctx) =>
        {
            await ctx.StepAsync("step1", () =>
            {
                Interlocked.Increment(ref stepInvocations);
                return Task.FromResult<object>(new { v = 1 });
            });

            // Fail on the first handler invocation (after caching the step).
            if (Interlocked.Increment(ref handlerInvocations) == 1)
                throw new InvalidOperationException("Simulated failure after step");
        });

        var spawn = await client.SpawnAsync<object>(
            "cached", new { }, new SpawnOptions { MaxAttempts = 2 });

        // Attempt 1: step fn called, then handler throws.
        await client.WorkBatchAsync(claimTimeoutSeconds: 30, batchSize: 1);

        // Attempt 2: step is cached, fn NOT called, handler returns normally.
        var result = await RunUntilDoneAsync(client, spawn.TaskId);

        Assert.Equal("completed", result.State);
        Assert.Equal(1, stepInvocations);     // step fn called exactly once
        Assert.Equal(2, handlerInvocations);  // handler called twice
    }

    // ── 8.4 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IdempotentSpawn_SameKeyReturnsSameTask()
    {
        var queue = Q();
        await using var client = fixture.CreateClient(queue);
        await client.CreateQueueAsync(queue);

        var key = "idem-" + Guid.NewGuid().ToString("N");

        var first  = await client.SpawnAsync<object>("idem-task", new { }, new SpawnOptions { IdempotencyKey = key });
        var second = await client.SpawnAsync<object>("idem-task", new { }, new SpawnOptions { IdempotencyKey = key });

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.TaskId, second.TaskId);
    }

    // ── 8.5 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SleepFor_TaskSuspendsAndResumesAfterDuration()
    {
        var queue = Q();
        await using var client = fixture.CreateClient(queue);
        await client.CreateQueueAsync(queue);

        client.RegisterTask<object>("sleeper", async (_, ctx) =>
        {
            await ctx.SleepForAsync("nap", TimeSpan.FromMilliseconds(200));
            await ctx.StepAsync("done", () => Task.FromResult<object>(new { woke = true }));
        });

        var spawn = await client.SpawnAsync<object>("sleeper", new { });

        // First batch: task suspends on sleep.
        await client.WorkBatchAsync(claimTimeoutSeconds: 30, batchSize: 1);

        var mid = await client.FetchTaskResultAsync(spawn.TaskId);
        Assert.NotNull(mid);
        Assert.NotEqual("completed", mid.State); // sleeping or pending

        // Wait for the sleep duration to expire, then claim again.
        await Task.Delay(500);

        var result = await RunUntilDoneAsync(client, spawn.TaskId, maxBatches: 3);
        Assert.Equal("completed", result.State);
    }

    // ── 8.6 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AwaitEvent_TaskSuspendsAndResumesWithPayload()
    {
        var queue = Q();
        await using var client = fixture.CreateClient(queue);
        await client.CreateQueueAsync(queue);

        client.RegisterTask<object>("event-waiter", async (_, ctx) =>
        {
            var payload = await ctx.AwaitEventAsync("order.shipped");
            await ctx.StepAsync("record", () =>
                Task.FromResult<object>(new { received = payload }));
        });

        var spawn = await client.SpawnAsync<object>("event-waiter", new { });

        // First batch: task suspends waiting for the event.
        await client.WorkBatchAsync(claimTimeoutSeconds: 30, batchSize: 1);
        var mid = await client.FetchTaskResultAsync(spawn.TaskId);
        Assert.NotNull(mid);
        Assert.NotEqual("completed", mid.State);

        // Emit the event from outside the task.
        await client.EmitEventAsync("order.shipped", new { tracking = "XYZ-123" });

        // Second batch: task wakes and completes.
        var result = await RunUntilDoneAsync(client, spawn.TaskId, maxBatches: 3);
        Assert.Equal("completed", result.State);
    }

    // ── 8.7 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AwaitEvent_TimeoutThrowsWhenNoEventArrives()
    {
        var queue = Q();
        await using var client = fixture.CreateClient(queue);
        await client.CreateQueueAsync(queue);

        // Handler awaits an event with a 1-second timeout; no event is emitted.
        client.RegisterTask<object>("event-timeout", async (_, ctx) =>
            await ctx.AwaitEventAsync("never-sent", timeoutSeconds: 1));

        var spawn = await client.SpawnAsync<object>(
            "event-timeout", new { }, new SpawnOptions { MaxAttempts = 1 });

        // First batch: task suspends while waiting for the event.
        await client.WorkBatchAsync(claimTimeoutSeconds: 30, batchSize: 1);

        // Let the 1-second timeout expire.
        await Task.Delay(1500);

        // Second batch: task wakes, AbsurdTimeoutException is thrown, task fails.
        await client.WorkBatchAsync(claimTimeoutSeconds: 30, batchSize: 1);

        var snapshot = await client.AwaitTaskResultAsync(spawn.TaskId, timeoutSeconds: 5);
        Assert.Equal("failed", snapshot.State);
        Assert.NotNull(snapshot.Failure);
        Assert.Contains("AbsurdTimeoutException", snapshot.Failure.Value.GetRawText());
    }

    // ── 8.8 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CancelTask_RunningTaskTransitionsToCancelled()
    {
        var queue = Q();
        await using var client = fixture.CreateClient(queue);
        await client.CreateQueueAsync(queue);

        // Task that sleeps for a long time — so it's in a controllable sleeping state.
        client.RegisterTask<object>("cancellable", async (_, ctx) =>
            await ctx.SleepForAsync("wait", TimeSpan.FromHours(1)));

        var spawn = await client.SpawnAsync<object>("cancellable", new { });

        // Run: task suspends on the long sleep.
        await client.WorkBatchAsync(claimTimeoutSeconds: 30, batchSize: 1);

        // Cancel the task while it's sleeping.
        await client.CancelTaskAsync(spawn.TaskId);

        var snapshot = await client.FetchTaskResultAsync(spawn.TaskId);
        Assert.NotNull(snapshot);
        Assert.Equal("cancelled", snapshot.State);
    }

    // ── 8.9 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RetryTask_FailedTaskIsRequeued()
    {
        var queue = Q();
        await using var client = fixture.CreateClient(queue);
        await client.CreateQueueAsync(queue);

        var invocations = 0;

        client.RegisterTask<object>("retryable", (_, _) =>
        {
            if (Interlocked.Increment(ref invocations) == 1)
                throw new InvalidOperationException("Intentional failure on attempt 1");
            return Task.CompletedTask;
        });

        // Spawn with maxAttempts:1 to prevent automatic retry.
        var spawn = await client.SpawnAsync<object>(
            "retryable", new { }, new SpawnOptions { MaxAttempts = 1 });

        // Run: fails on first attempt, no auto-retry.
        await client.WorkBatchAsync(claimTimeoutSeconds: 30, batchSize: 1);

        var failed = await client.AwaitTaskResultAsync(spawn.TaskId, timeoutSeconds: 5);
        Assert.Equal("failed", failed.State);

        // Retry manually.
        var retryResult = await client.RetryTaskAsync(spawn.TaskId);
        Assert.Equal(spawn.TaskId, retryResult.TaskId);

        // Run again: succeeds.
        var final = await RunUntilDoneAsync(client, spawn.TaskId);
        Assert.Equal("completed", final.State);
        Assert.Equal(2, invocations);
    }

    // ── 8.10 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BeforeSpawnHook_HeaderInjectedIntoTask()
    {
        var queue = Q();
        await using var client = fixture.CreateClient(queue, new AbsurdOptions
        {
            Hooks = new InjectingHooks(() => "trace-abc"),
        });
        await client.CreateQueueAsync(queue);

        JsonElement? capturedTraceId = null;

        client.RegisterTask<object>("hook-spawn", (_, ctx) =>
        {
            capturedTraceId = ctx.Headers.TryGetValue("traceId", out var v) ? v : null;
            return Task.CompletedTask;
        });

        var spawn = await client.SpawnAsync<object>("hook-spawn", new { });
        await RunUntilDoneAsync(client, spawn.TaskId);

        Assert.NotNull(capturedTraceId);
        Assert.Equal("trace-abc", capturedTraceId.Value.GetString());
    }

    // ── 8.11 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WrapTaskExecutionHook_WrapperCalledAndHandlerExecutes()
    {
        var queue = Q();
        var wrapperCalled = false;

        await using var client = fixture.CreateClient(queue, new AbsurdOptions
        {
            Hooks = new WrappingHooks(() => wrapperCalled = true),
        });
        await client.CreateQueueAsync(queue);

        client.RegisterTask<object>("hook-wrap", (_, _) => Task.CompletedTask);

        var spawn = await client.SpawnAsync<object>("hook-wrap", new { });
        var result = await RunUntilDoneAsync(client, spawn.TaskId);

        Assert.Equal("completed", result.State);
        Assert.True(wrapperCalled);
    }

    // ── 8.12 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BindToConnection_SpawnInsideRolledBackTransactionIsNotVisible()
    {
        var queue = Q();
        await using var client = fixture.CreateClient(queue);
        await client.CreateQueueAsync(queue);

        // Open a raw connection and start a transaction.
        await using var con = await fixture.DataSource.OpenConnectionAsync();
        await using var tx  = await con.BeginTransactionAsync();

        var bound = client.BindToConnection(con, tx);
        var spawn = await bound.SpawnAsync<object>("tx-task", new { });
        Assert.True(spawn.Created);

        // Roll back — the task row is never committed.
        await tx.RollbackAsync();

        // Querying with the regular client should find nothing.
        var snapshot = await client.FetchTaskResultAsync(spawn.TaskId);
        Assert.Null(snapshot);
    }

    // ── 8.13 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WorkerConcurrency_TwoTasksRunInParallel()
    {
        var queue = Q();
        await using var client = fixture.CreateClient(queue);
        await client.CreateQueueAsync(queue);

        // Coordination primitives: tasks signal "entered" then wait for "release".
        using var entered = new SemaphoreSlim(0);
        using var release = new SemaphoreSlim(0);

        client.RegisterTask<object>("parallel", async (_, _) =>
        {
            entered.Release();
            await release.WaitAsync();
        });

        await client.SpawnAsync<object>("parallel", new { });
        await client.SpawnAsync<object>("parallel", new { });

        var worker = await client.StartWorkerAsync(new WorkerOptions
        {
            Concurrency = 2,
            FatalOnLeaseTimeout = false, // never exit the process in tests
        });

        try
        {
            // Both tasks must enter concurrently within 10 seconds.
            Assert.True(await entered.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(await entered.WaitAsync(TimeSpan.FromSeconds(10)));

            // Let both tasks finish.
            release.Release(2);
        }
        finally
        {
            await worker.StopAsync();
        }
    }

    // ── 8.14 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WorkBatch_ClaimsAndRunsTasksThenReturns()
    {
        var queue = Q();
        await using var client = fixture.CreateClient(queue);
        await client.CreateQueueAsync(queue);

        client.RegisterTask<object>("batch-task", (_, _) => Task.CompletedTask);

        var s1 = await client.SpawnAsync<object>("batch-task", new { });
        var s2 = await client.SpawnAsync<object>("batch-task", new { });

        // WorkBatch should claim both and return only after they complete.
        await client.WorkBatchAsync(batchSize: 2, claimTimeoutSeconds: 30);

        var r1 = await client.FetchTaskResultAsync(s1.TaskId);
        var r2 = await client.FetchTaskResultAsync(s2.TaskId);

        Assert.Equal("completed", r1!.State);
        Assert.Equal("completed", r2!.State);
    }
}
