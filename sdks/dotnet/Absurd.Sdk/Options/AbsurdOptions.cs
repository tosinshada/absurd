using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Absurd.Options;

/// <summary>
/// Configuration options for the <see cref="AbsurdClient"/> class.
/// </summary>
public sealed class AbsurdOptions
{
    /// <summary>
    /// Postgres connection string. Falls back to <c>ABSURD_DATABASE_URL</c>,
    /// then <c>PGDATABASE</c>, then <c>postgresql://localhost/absurd</c>.
    /// Ignored when <see cref="DataSource"/> is set.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// An existing <see cref="NpgsqlDataSource"/> to use for all connections.
    /// When supplied the SDK will NOT dispose it on <see cref="AbsurdClient.DisposeAsync"/>.
    /// </summary>
    public NpgsqlDataSource? DataSource { get; set; }

    /// <summary>
    /// Default queue name for all operations. Defaults to <c>"default"</c>.
    /// </summary>
    public string QueueName { get; set; } = "default";

    /// <summary>
    /// Default maximum number of attempts for spawned tasks. Defaults to <c>5</c>.
    /// </summary>
    public int DefaultMaxAttempts { get; set; } = 5;

    /// <summary>
    /// Logger used by the SDK. Defaults to <see cref="NullLogger"/>.
    /// </summary>
    public ILogger? Log { get; set; }

    /// <summary>
    /// Optional lifecycle hooks for tracing and context propagation.
    /// </summary>
    public IAbsurdHooks? Hooks { get; set; }
}
