using LoanBooking.Worker.Workflow;

namespace LoanBooking.Worker.Services;

/// <summary>
/// Mock lien placement and reversion service.
/// Simulates a ~5% failure rate on placement.
/// </summary>
public static class LienService
{
    public static LienResult PlaceLien(Guid loanId, string collateralId)
    {
        if (Random.Shared.NextDouble() < 0.05)
        {
            return new LienResult
            {
                Success = false,
                Reason  = "Collateral registry temporarily unavailable",
            };
        }

        return new LienResult
        {
            Success       = true,
            LienReference = $"LIEN-{collateralId.ToUpperInvariant()}-{loanId.ToString()[..8].ToUpperInvariant()}",
        };
    }

    public static void RevertLien(string lienReference, ILogger logger)
    {
        logger.LogInformation("[lien] Lien {LienReference} successfully reverted", lienReference);
    }
}
