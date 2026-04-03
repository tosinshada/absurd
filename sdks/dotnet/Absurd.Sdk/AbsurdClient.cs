using System.Data;
using System.Data.Common;
using System.Text.Json;
using Absurd.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Absurd;

/// <summary>
/// The Absurd SDK client. Create one instance per application and keep it for the
/// lifetime of the process.
/// </summary>
public sealed class AbsurdClient : IAsyncDisposable
{
    private readonly NpgsqlDataSource? _ownedDataSource;
    private readonly NpgsqlDataSource? _externalDataSource;

    // For bind-to-connection scenarios only
    private readonly DbConnection? _boundConnection;
    private readonly DbTransaction? _boundTransaction;

    internal readonly string QueueName;
    internal readonly int DefaultMaxAttempts;
    internal readonly ILogger Log;
    internal readonly IAbsurdHooks? Hooks;

    private readonly Dictionary<string, RegisteredTask> _registry = new();

    internal static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <summary>Creates a new <see cref="AbsurdClient"/> from <see cref="AbsurdOptions"/>.</summary>
    public AbsurdClient(AbsurdOptions? options = null)
    {
        options ??= new AbsurdOptions();

        QueueName = ValidateQueueName(options.QueueName);
        DefaultMaxAttempts = options.DefaultMaxAttempts;
        Log = options.Log ?? NullLogger.Instance;
        Hooks = options.Hooks;

        if (options.DataSource is not null)
        {
            _externalDataSource = options.DataSource;
        }
        else
        {
            var connectionString =
                options.ConnectionString
                ?? Environment.GetEnvironmentVariable("ABSURD_DATABASE_URL")
                ?? Environment.GetEnvironmentVariable("PGDATABASE")
                ?? "postgresql://localhost/absurd";

            _ownedDataSource = NpgsqlDataSource.Create(connectionString);
        }
    }

    private AbsurdClient(
        AbsurdClient parent,
        DbConnection boundConnection,
        DbTransaction? boundTransaction)
    {
        QueueName = parent.QueueName;
        DefaultMaxAttempts = parent.DefaultMaxAttempts;
        Log = parent.Log;
        Hooks = parent.Hooks;
        _registry = parent._registry;
        _boundConnection = boundConnection;
        _boundTransaction = boundTransaction;
    }

    // -------------------------------------------------------------------------
    // Task Registration (phase 5)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers a typed task handler. The handler is invoked by the worker
    /// when a task with <paramref name="name"/> is claimed.
    /// </summary>
    public void RegisterTask<TParams>(
        string name,
        Func<TParams, TaskContext, Task> handler,
        TaskRegistrationOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var queue = ValidateQueueName(options?.Queue ?? QueueName);
        var maxAttempts = options?.DefaultMaxAttempts;
        if (maxAttempts is < 1)
            throw new ArgumentException("DefaultMaxAttempts must be at least 1.", nameof(options));

        _registry[name] = new RegisteredTask
        {
            Name = name,
            Queue = queue,
            DefaultMaxAttempts = maxAttempts,
            DefaultCancellation = options?.DefaultCancellation,
            Handler = (paramsJson, ctx) =>
            {
                var typedParams = paramsJson.Deserialize<TParams>(JsonOptions)
                    ?? throw new InvalidOperationException($"Failed to deserialise params for task '{name}'.");
                return handler(typedParams, ctx);
            },
        };
    }

    // -------------------------------------------------------------------------
    // Connection helpers (used by all client methods and worker)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a new <see cref="AbsurdClient"/> that routes all operations through
    /// <paramref name="connection"/> and optional <paramref name="transaction"/>.
    /// The caller owns the connection's lifecycle — this client will never close it.
    /// </summary>
    public AbsurdClient BindToConnection(DbConnection connection, DbTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return new AbsurdClient(this, connection, transaction);
    }

    /// <summary>Opens a fresh connection from the data source.</summary>
    internal async Task<(NpgsqlConnection con, bool owned)> OpenConnectionAsync(CancellationToken ct = default)
    {
        if (_boundConnection is NpgsqlConnection bound)
        {
            if (bound.State != ConnectionState.Open)
                await bound.OpenAsync(ct);
            return (bound, owned: false);
        }

        var ds = _ownedDataSource ?? _externalDataSource
            ?? throw new InvalidOperationException("No data source available.");
        return (await ds.OpenConnectionAsync(ct), owned: true);
    }

    internal DbTransaction? CurrentTransaction => _boundTransaction;

    internal bool IsBound => _boundConnection is not null;

    internal Dictionary<string, RegisteredTask> Registry => _registry;

    // -------------------------------------------------------------------------
    // Dispose
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        // Never dispose a bound connection — the caller owns it.
        if (_ownedDataSource is not null)
            await _ownedDataSource.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // Validation
    // -------------------------------------------------------------------------

    private const int MaxQueueNameBytes = 57;

    internal static string ValidateQueueName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Queue name must not be empty.");
        if (System.Text.Encoding.UTF8.GetByteCount(name) > MaxQueueNameBytes)
            throw new ArgumentException($"Queue name \"{name}\" is too long (max {MaxQueueNameBytes} bytes).");
        return name;
    }
}
