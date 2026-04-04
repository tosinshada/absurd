using Absurd;
using Absurd.DependencyInjection;
using Absurd.Dashboard.DependencyInjection;
using LoanBooking.Data;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Absurd")
    ?? "Host=localhost;Database=absurd";

// Absurd SDK for spawning tasks and fetching results
builder.Services.AddAbsurd(opts =>
{
    opts.ConnectionString = connectionString;
    opts.QueueName = "loan-booking";
});

// Absurd embedded dashboard
builder.Services.AddAbsurdDashboard(opts =>
    opts.ConnectionString = connectionString);

// Application-level Postgres data source for the loans table
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<LoanDatabase>();

builder.Services.AddControllers();

var app = builder.Build();

app.UseHttpsRedirection();

// Mount the Absurd monitoring dashboard at /habitat
app.MapAbsurdDashboard("/habitat");

app.MapControllers();

// One-time startup: ensure loans table and queue exist
var db = app.Services.GetRequiredService<LoanDatabase>();
await db.EnsureLoansTableAsync();

var absurdClient = app.Services.GetRequiredService<AbsurdClient>();
await absurdClient.CreateQueueAsync("loan-booking");

app.Run();
