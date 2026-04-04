using LoanBooking.Worker.Workflow;
using Npgsql;
using NpgsqlTypes;

namespace LoanBooking.Worker.Data;

/// <summary>
/// Application-level data access for the loans table inside workflow steps.
/// Each method opens its own connection from the shared data source so that it
/// is independent from the Absurd SDK's connection used for checkpointing.
/// </summary>
public sealed class LoanDatabase
{
    private readonly NpgsqlDataSource _ds;

    public LoanDatabase(NpgsqlDataSource dataSource) => _ds = dataSource;

    /// <summary>Creates the loans table when it does not already exist.</summary>
    public static async Task EnsureLoansTableAsync(NpgsqlDataSource ds, CancellationToken ct = default)
    {
        await using var con = await ds.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS loans (
                id               UUID           PRIMARY KEY DEFAULT gen_random_uuid(),
                applicant_id     TEXT           NOT NULL,
                amount           NUMERIC(15, 2) NOT NULL,
                purpose          TEXT           NOT NULL,
                collateral_id    TEXT           NOT NULL,
                status           TEXT           NOT NULL DEFAULT 'pending',
                credit_score     INTEGER,
                rejection_reason TEXT,
                lien_reference   TEXT,
                disbursed_at     TIMESTAMPTZ,
                task_id          TEXT           UNIQUE,
                created_at       TIMESTAMPTZ    NOT NULL DEFAULT NOW()
            )
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Inserts a new loan record. Uses ON CONFLICT to handle step retries safely –
    /// if the task is retried the existing record is returned unchanged.
    /// </summary>
    public async Task<LoanInsertResult> InsertLoanAsync(
        string applicantId, decimal amount, string purpose, string collateralId, string taskId)
    {
        await using var con = await _ds.OpenConnectionAsync();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = """
            INSERT INTO loans (applicant_id, amount, purpose, collateral_id, task_id)
            VALUES ($1, $2, $3, $4, $5)
            ON CONFLICT (task_id) DO UPDATE SET task_id = EXCLUDED.task_id
            RETURNING id, created_at
            """;
        cmd.Parameters.Add(new NpgsqlParameter { Value = applicantId });
        cmd.Parameters.Add(new NpgsqlParameter { Value = amount });
        cmd.Parameters.Add(new NpgsqlParameter { Value = purpose });
        cmd.Parameters.Add(new NpgsqlParameter { Value = collateralId });
        cmd.Parameters.Add(new NpgsqlParameter { Value = taskId });

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new LoanInsertResult
        {
            LoanId    = reader.GetGuid(0),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(1),
        };
    }

    /// <summary>Updates the loan status and optionally the credit score.</summary>
    public async Task UpdateLoanStatusAsync(Guid loanId, string status, int? creditScore = null)
    {
        await using var con = await _ds.OpenConnectionAsync();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = """
            UPDATE loans
            SET status       = $1,
                credit_score = COALESCE($2, credit_score)
            WHERE id = $3
            """;
        cmd.Parameters.Add(new NpgsqlParameter { Value = status });
        cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)creditScore ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter { Value = loanId, NpgsqlDbType = NpgsqlDbType.Uuid });
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Marks the loan as rejected with an optional reason.</summary>
    public async Task UpdateLoanRejectionAsync(
        Guid loanId, string status, string? reason, bool clearLien = false)
    {
        await using var con = await _ds.OpenConnectionAsync();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = clearLien
            ? """
              UPDATE loans
              SET status           = $1,
                  rejection_reason = $2,
                  lien_reference   = NULL
              WHERE id = $3
              """
            : """
              UPDATE loans
              SET status           = $1,
                  rejection_reason = $2
              WHERE id = $3
              """;
        cmd.Parameters.Add(new NpgsqlParameter { Value = status });
        cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)reason ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter { Value = loanId, NpgsqlDbType = NpgsqlDbType.Uuid });
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Stores the lien reference and advances status to lien_placed.</summary>
    public async Task UpdateLienAsync(Guid loanId, string lienReference)
    {
        await using var con = await _ds.OpenConnectionAsync();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = """
            UPDATE loans
            SET status         = 'lien_placed',
                lien_reference = $1
            WHERE id = $2
            """;
        cmd.Parameters.Add(new NpgsqlParameter { Value = lienReference });
        cmd.Parameters.Add(new NpgsqlParameter { Value = loanId, NpgsqlDbType = NpgsqlDbType.Uuid });
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Marks the loan as successfully disbursed.</summary>
    public async Task UpdateLoanDisbursedAsync(
        Guid loanId, string transactionId, DateTimeOffset disbursedAt)
    {
        await using var con = await _ds.OpenConnectionAsync();
        await using var cmd = con.CreateCommand();
        cmd.CommandText = """
            UPDATE loans
            SET status       = 'disbursed',
                disbursed_at = $1
            WHERE id = $2
            """;
        cmd.Parameters.Add(new NpgsqlParameter { Value = disbursedAt });
        cmd.Parameters.Add(new NpgsqlParameter { Value = loanId, NpgsqlDbType = NpgsqlDbType.Uuid });
        await cmd.ExecuteNonQueryAsync();
    }
}
