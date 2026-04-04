using System.Text.Json.Serialization;

namespace LoanBooking.Worker.Workflow;

/// <summary>Parameters passed to the loan-booking-workflow task.</summary>
public sealed class LoanWorkflowParams
{
    [JsonPropertyName("applicantId")]
    public required string ApplicantId { get; init; }

    [JsonPropertyName("amount")]
    public required decimal Amount { get; init; }

    [JsonPropertyName("purpose")]
    public required string Purpose { get; init; }

    [JsonPropertyName("collateralId")]
    public required string CollateralId { get; init; }
}

/// <summary>Checkpoint data stored after the insert-loan-request step.</summary>
public sealed class LoanInsertResult
{
    public required Guid LoanId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Result of the credit check step.</summary>
public sealed class CreditCheckResult
{
    public required bool Approved { get; init; }
    public required int CreditScore { get; init; }
    public string? Reason { get; init; }
}

/// <summary>Result of the lender product check step.</summary>
public sealed class LenderCheckResult
{
    public required bool CanFund { get; init; }
    public string? ProductId { get; init; }
    public decimal? InterestRate { get; init; }
    public string? Reason { get; init; }
}

/// <summary>Result of the lien placement step.</summary>
public sealed class LienResult
{
    public required bool Success { get; init; }
    public string? LienReference { get; init; }
    public string? Reason { get; init; }
}

/// <summary>Result of the loan disbursal step.</summary>
public sealed class DisbursalResult
{
    public required bool Success { get; init; }
    public string? TransactionId { get; init; }
    public DateTimeOffset? DisbursedAt { get; init; }
    public string? Reason { get; init; }
}
