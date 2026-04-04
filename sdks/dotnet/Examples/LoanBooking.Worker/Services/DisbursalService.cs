using LoanBooking.Worker.Workflow;

namespace LoanBooking.Worker.Services;

/// <summary>
/// Mock loan disbursal service.
/// Simulates a ~10% failure rate.
/// </summary>
public static class DisbursalService
{
    public static DisbursalResult Disburse(Guid loanId)
    {
        if (Random.Shared.NextDouble() < 0.10)
        {
            return new DisbursalResult
            {
                Success = false,
                Reason  = "Payment rail temporarily unavailable",
            };
        }

        return new DisbursalResult
        {
            Success       = true,
            TransactionId = $"TXN-{loanId.ToString()[..8].ToUpperInvariant()}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            DisbursedAt   = DateTimeOffset.UtcNow,
        };
    }
}
