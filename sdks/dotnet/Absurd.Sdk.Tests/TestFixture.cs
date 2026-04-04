using Absurd.Options;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Absurd.Tests;

/// <summary>
/// Shared test fixture that starts a PostgreSQL container, applies the Absurd schema,
/// and exposes helpers for creating isolated <see cref="AbsurdClient"/> instances.
/// </summary>
public sealed class TestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    /// <summary>Connection string to the running Postgres instance.</summary>
    public string ConnectionString { get; private set; } = "";

    /// <summary>Pooled data source shared across all tests in a session.</summary>
    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
        DataSource = NpgsqlDataSource.Create(ConnectionString);

        // Apply the Absurd schema
        var sqlPath = FindAbsurdSql();
        var sql = await File.ReadAllTextAsync(sqlPath);

        await using var con = await DataSource.OpenConnectionAsync();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Creates an <see cref="AbsurdClient"/> that shares the fixture's connection pool.
    /// The fixture owns the <see cref="NpgsqlDataSource"/>; callers must dispose the client.
    /// </summary>
    public AbsurdClient CreateClient(string queueName, AbsurdOptions? options = null)
    {
        var opts = options ?? new AbsurdOptions();
        // Pass the shared DataSource; the client treats it as external and won't dispose it.
        opts.DataSource = DataSource;
        opts.QueueName = queueName;
        return new AbsurdClient(opts);
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
