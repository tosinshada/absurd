using LoanBooking.Worker.Workflow;

namespace LoanBooking.Worker.Services;

/// <summary>
/// Mock credit-check service.
/// Generates a deterministic score from the applicant ID so results are
/// consistent across retries. Score >= 650 passes; below 650 fails.
/// </summary>
public static class CreditCheckService
{
    public static CreditCheckResult Check(string applicantId)
    {
        var score = DeterministicScore(applicantId);
        var approved = score >= 650;

        return new CreditCheckResult
        {
            Approved    = approved,
            CreditScore = score,
            Reason      = approved ? null : "Credit score below minimum threshold (650)",
        };
    }

    private static int DeterministicScore(string applicantId)
    {
        var hash = 0;
        foreach (var c in applicantId)
            hash = (hash * 31 + c) & 0x7FFFFFFF;
        // Map to 500–800 range
        return 500 + (hash % 301);
    }
}
