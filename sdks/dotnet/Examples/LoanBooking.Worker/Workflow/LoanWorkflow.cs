using Absurd;
using Absurd.Options;
using LoanBooking.Worker.Data;
using LoanBooking.Worker.Services;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LoanBooking.Worker.Workflow;

/// <summary>
/// Registers and implements the loan-booking-workflow durable task.
///
/// Steps:
///   1. insert-loan-request      – persist loan record in DB
///   2. credit-check             – call credit bureau (mocked)
///      └─ send-rejection-email  – compensating step on credit failure
///   3. check-lender-product     – verify lender can fund the loan
///   4. place-lien               – register lien on collateral (mocked)
///   5. disburse-loan            – transfer funds (mocked)
///      └─ revert-lien           – compensating step on disbursal failure
/// </summary>
public static class LoanWorkflow
{
    public const string TaskName = "loan-booking-workflow";

    public static void Register(AbsurdClient client, NpgsqlDataSource dataSource, ILogger logger)
    {
        client.RegisterTask<LoanWorkflowParams>(
            TaskName,
            (p, ctx) => ExecuteAsync(p, ctx, dataSource, logger),
            new TaskRegistrationOptions
            {
                Name               = TaskName,
                DefaultMaxAttempts = 3,
            });
    }

    private static async Task ExecuteAsync(
        LoanWorkflowParams p,
        TaskContext ctx,
        NpgsqlDataSource dataSource,
        ILogger logger)
    {
        var db = new LoanDatabase(dataSource);

        // ── Step 1: Insert loan request ────────────────────────────────────
        var loanRecord = await ctx.StepAsync("insert-loan-request", async () =>
        {
            var result = await db.InsertLoanAsync(
                p.ApplicantId, p.Amount, p.Purpose, p.CollateralId, ctx.TaskId.ToString());
            logger.LogInformation("[{TaskId}] Loan request inserted → loan_id={LoanId}",
                ctx.TaskId, result.LoanId);
            return result;
        });

        // ── Step 2: Credit check ───────────────────────────────────────────
        var creditResult = await ctx.StepAsync("credit-check", async () =>
        {
            logger.LogInformation("[{TaskId}] Running credit check for applicant={ApplicantId}",
                ctx.TaskId, p.ApplicantId);
            var result = CreditCheckService.Check(p.ApplicantId);
            await db.UpdateLoanStatusAsync(
                loanRecord.LoanId,
                result.Approved ? "credit_check_passed" : "credit_check_failed",
                creditScore: result.CreditScore);
            logger.LogInformation("[{TaskId}] Credit check → score={Score} approved={Approved}",
                ctx.TaskId, result.CreditScore, result.Approved);
            return result;
        });

        // ── Step 2a: Rejection path ────────────────────────────────────────
        if (!creditResult.Approved)
        {
            await ctx.StepAsync("send-rejection-email", async () =>
            {
                var reason = creditResult.Reason ?? "Credit check failed";
                EmailService.SendRejection(p.ApplicantId, reason, logger);
                await db.UpdateLoanRejectionAsync(loanRecord.LoanId, "credit_check_failed", reason);
                return true;
            });
            return;
        }

        // ── Step 3: Check lender product ───────────────────────────────────
        var lenderResult = await ctx.StepAsync("check-lender-product", async () =>
        {
            logger.LogInformation("[{TaskId}] Checking lender product for amount={Amount} purpose=\"{Purpose}\"",
                ctx.TaskId, p.Amount, p.Purpose);
            var result = LenderService.CheckProduct(p.Amount, p.Purpose);
            if (!result.CanFund)
                await db.UpdateLoanRejectionAsync(loanRecord.LoanId, "lender_check_failed", result.Reason);
            return result;
        });

        if (!lenderResult.CanFund)
            return;

        // ── Step 4: Place lien on collateral ───────────────────────────────
        var lienResult = await ctx.StepAsync("place-lien", async () =>
        {
            logger.LogInformation("[{TaskId}] Placing lien on collateral={CollateralId}",
                ctx.TaskId, p.CollateralId);
            var result = LienService.PlaceLien(loanRecord.LoanId, p.CollateralId);
            if (result.Success)
            {
                await db.UpdateLienAsync(loanRecord.LoanId, result.LienReference!);
                logger.LogInformation("[{TaskId}] Lien placed → reference={LienRef}",
                    ctx.TaskId, result.LienReference);
            }
            return result;
        });

        if (!lienResult.Success)
        {
            await db.UpdateLoanRejectionAsync(loanRecord.LoanId, "lien_failed", lienResult.Reason);
            return;
        }

        // ── Step 5: Disburse loan ──────────────────────────────────────────
        var disbursalResult = await ctx.StepAsync("disburse-loan", async () =>
        {
            logger.LogInformation("[{TaskId}] Disbursing loan={LoanId}",
                ctx.TaskId, loanRecord.LoanId);
            return DisbursalService.Disburse(loanRecord.LoanId);
        });

        // ── Step 5a: Revert lien on disbursal failure ──────────────────────
        if (!disbursalResult.Success)
        {
            await ctx.StepAsync("revert-lien", async () =>
            {
                logger.LogInformation("[{TaskId}] Disbursal failed – reverting lien={LienRef}",
                    ctx.TaskId, lienResult.LienReference);
                LienService.RevertLien(lienResult.LienReference!, logger);
                await db.UpdateLoanRejectionAsync(
                    loanRecord.LoanId, "disbursal_failed", disbursalResult.Reason, clearLien: true);
                return true;
            });
            return;
        }

        // ── Final: update disbursed status ─────────────────────────────────
        await db.UpdateLoanDisbursedAsync(
            loanRecord.LoanId,
            disbursalResult.TransactionId!,
            disbursalResult.DisbursedAt!.Value);

        logger.LogInformation("[{TaskId}] Loan disbursed → transaction={TxnId}",
            ctx.TaskId, disbursalResult.TransactionId);
    }
}
