using Absurd;
using Absurd.DependencyInjection;
using LoanBooking.Worker;
using Npgsql;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Absurd")
    ?? "Host=localhost;Database=absurd";

// Application-level Postgres data source (for loan table writes inside steps)
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));

// Absurd SDK client
builder.Services.AddAbsurd(opts =>
{
    opts.ConnectionString = connectionString;
    opts.QueueName = "loan-booking";
});

// Hosted service that sets up the queue and runs the worker poll loop
builder.Services.AddHostedService<LoanWorkerService>();

await builder.Build().RunAsync();
