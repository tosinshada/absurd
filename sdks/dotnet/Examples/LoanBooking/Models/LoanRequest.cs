namespace LoanBooking.Models;

public sealed class LoanRequest
{
    public required string ApplicantId { get; init; }
    public required decimal Amount { get; init; }
    public required string Purpose { get; init; }
    public required string CollateralId { get; init; }
}
