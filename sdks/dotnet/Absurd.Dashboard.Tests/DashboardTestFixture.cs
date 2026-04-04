using Absurd.Dashboard.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Absurd.Dashboard.Tests;

[CollectionDefinition("DashboardIntegration")]
public sealed class DashboardIntegrationCollection : ICollectionFixture<DashboardTestFixture> { }

/// <summary>
/// Shared test fixture: starts a PostgreSQL container, applies the Absurd schema,
/// and creates an in-process <see cref="TestServer"/> with the dashboard middleware.
/// </summary>
public sealed class DashboardTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:16-alpine").Build();

    /// <summary>In-process test server with the dashboard mounted at <c>/habitat</c>.</summary>
    public TestServer Server { get; private set; } = null!;

    /// <summary>Pre-configured client for the test server.</summary>
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var connectionString = _container.GetConnectionString();

        // Apply the Absurd schema
        await ApplySchemaAsync(connectionString);

        // Build a minimal ASP.NET Core host with the dashboard
        var host = new WebHostBuilder()
            .ConfigureServices(services =>
                services.AddAbsurdDashboard(opts => opts.ConnectionString = connectionString))
            .Configure(app =>
                app.MapAbsurdDashboard("/habitat"));

        Server = new TestServer(host);
        Client = Server.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        Server.Dispose();
        await _container.DisposeAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static async Task ApplySchemaAsync(string connectionString)
    {
        var sqlPath = FindAbsurdSql();
        var sql = await File.ReadAllTextAsync(sqlPath);

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static string FindAbsurdSql()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "sql", "absurd.sql");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "Cannot locate sql/absurd.sql. Searched upward from: " + AppContext.BaseDirectory);
    }
}
