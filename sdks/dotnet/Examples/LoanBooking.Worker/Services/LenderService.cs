using LoanBooking.Worker.Workflow;

namespace LoanBooking.Worker.Services;

/// <summary>
/// Mock lender product check.
/// Verifies the lender has a suitable product for the requested amount and purpose.
/// Loans above $500,000 exceed lender capacity.
/// </summary>
public static class LenderService
{
    public static LenderCheckResult CheckProduct(decimal amount, string purpose)
    {
        if (amount > 500_000m)
        {
            return new LenderCheckResult
            {
                CanFund = false,
                Reason  = "Requested amount exceeds lender capacity ($500,000 limit)",
            };
        }

        var prefix = new string(
            purpose.ToUpperInvariant().Replace(" ", "-").Take(6).ToArray());

        return new LenderCheckResult
        {
            CanFund      = true,
            ProductId    = $"PROD-{prefix}-001",
            InterestRate = amount > 100_000m ? 8.5m : 7.5m,
        };
    }
}
