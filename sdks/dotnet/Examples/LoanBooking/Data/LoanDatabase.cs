using LoanBooking.Models;
using Npgsql;
using NpgsqlTypes;

namespace LoanBooking.Data;

/// <summary>
/// Application-level data access for the loans table.
/// The Absurd SDK manages its own connection for workflow checkpointing;
/// this class handles direct loan record queries.
/// </summary>
public sealed class LoanDatabase
{
    private readonly NpgsqlDataSource _ds;

    public LoanDatabase(NpgsqlDataSource dataSource) => _ds = dataSource;

    public async Task EnsureLoansTableAsync(CancellationToken ct = default)
    {
        await using var con = await _ds.OpenConnectionAsync(ct);
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

    public async Task<LoanRecord?> GetLoanAsync(Guid id, CancellationToken ct = default)
    {
        await using var con = await _ds.OpenConnectionAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT id, applicant_id, amount, purpose, collateral_id,
                   status, credit_score, rejection_reason,
                   lien_reference, disbursed_at, task_id, created_at
            FROM loans
            WHERE id = $1
            """;
        cmd.Parameters.Add(new NpgsqlParameter { Value = id, NpgsqlDbType = NpgsqlDbType.Uuid });
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return new LoanRecord
        {
            Id               = reader.GetGuid(0),
            ApplicantId      = reader.GetString(1),
            Amount           = reader.GetDecimal(2),
            Purpose          = reader.GetString(3),
            CollateralId     = reader.GetString(4),
            Status           = reader.GetString(5),
            CreditScore      = reader.IsDBNull(6)  ? null : reader.GetInt32(6),
            RejectionReason  = reader.IsDBNull(7)  ? null : reader.GetString(7),
            LienReference    = reader.IsDBNull(8)  ? null : reader.GetString(8),
            DisbursedAt      = reader.IsDBNull(9)  ? null : reader.GetFieldValue<DateTimeOffset>(9),
            TaskId           = reader.IsDBNull(10) ? null : reader.GetString(10),
            CreatedAt        = reader.GetFieldValue<DateTimeOffset>(11),
        };
    }
}
