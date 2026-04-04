using Absurd;
using Absurd.Options;
using LoanBooking.Worker.Data;
using LoanBooking.Worker.Workflow;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LoanBooking.Worker;

/// <summary>
/// Background service that initialises the queue, registers the loan workflow,
/// and runs the Absurd worker poll loop for the lifetime of the process.
/// </summary>
public sealed class LoanWorkerService : BackgroundService
{
    private readonly AbsurdClient _client;
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<LoanWorkerService> _logger;

    public LoanWorkerService(
        AbsurdClient client,
        NpgsqlDataSource dataSource,
        ILogger<LoanWorkerService> logger)
    {
        _client     = client;
        _dataSource = dataSource;
        _logger     = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Ensure the loans table and Absurd queue exist (idempotent)
        await LoanDatabase.EnsureLoansTableAsync(_dataSource, stoppingToken);
        await _client.CreateQueueAsync("loan-booking", stoppingToken);

        // Register the loan-booking-workflow handler before starting the poll loop
        LoanWorkflow.Register(_client, _dataSource, _logger);

        _logger.LogInformation("Loan booking worker started – polling queue 'loan-booking'");

        var worker = await _client.StartWorkerAsync(new WorkerOptions
        {
            Concurrency = 4,
            OnError = ex =>
            {
                _logger.LogError(ex, "[worker] Unhandled workflow error");
                return Task.CompletedTask;
            },
        });

        // Hold until the host signals shutdown
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }

        _logger.LogInformation("Loan booking worker stopping – draining in-flight tasks...");
        await worker.StopAsync();
    }
}
