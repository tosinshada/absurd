namespace LoanBooking.Models;

public sealed class LoanRecord
{
    public required Guid Id { get; init; }
    public required string ApplicantId { get; init; }
    public required decimal Amount { get; init; }
    public required string Purpose { get; init; }
    public required string CollateralId { get; init; }
    public required string Status { get; init; }
    public int? CreditScore { get; init; }
    public string? RejectionReason { get; init; }
    public string? LienReference { get; init; }
    public DateTimeOffset? DisbursedAt { get; init; }
    public string? TaskId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
