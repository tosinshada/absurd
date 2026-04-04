using Absurd;
using LoanBooking.Data;
using LoanBooking.Models;
using Microsoft.AspNetCore.Mvc;

namespace LoanBooking.Controllers;

[ApiController]
[Route("loans")]
public sealed class LoansController : ControllerBase
{
    private readonly IAbsurdClient _absurd;
    private readonly LoanDatabase _db;

    public LoansController(IAbsurdClient absurd, LoanDatabase db)
    {
        _absurd = absurd;
        _db = db;
    }

    // POST /loans – submit a loan application and start the durable workflow
    [HttpPost]
    public async Task<IActionResult> CreateLoan([FromBody] LoanRequest request, CancellationToken ct)
    {
        if (request.Amount <= 0)
            return BadRequest(new { error = "amount must be a positive number" });

        var spawned = await _absurd.SpawnAsync(
            "loan-booking-workflow",
            new
            {
                applicantId    = request.ApplicantId,
                amount         = request.Amount,
                purpose        = request.Purpose,
                collateralId   = request.CollateralId,
            },
            ct: ct);

        return Accepted(new
        {
            task_id = spawned.TaskId,
            run_id  = spawned.RunId,
            message = "Loan booking workflow started",
        });
    }

    // GET /loans/{loanId} – fetch the loan record from the database
    [HttpGet("{loanId:guid}")]
    public async Task<IActionResult> GetLoan(Guid loanId, CancellationToken ct)
    {
        var loan = await _db.GetLoanAsync(loanId, ct);
        if (loan is null) return NotFound(new { error = "Loan not found" });
        return Ok(loan);
    }
}
