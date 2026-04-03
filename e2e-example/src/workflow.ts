import { Absurd } from "absurd-sdk";
import { pool } from "./db.js";
import { performCreditCheck } from "./services/credit-check.js";
import { checkLenderProduct } from "./services/lender.js";
import { placeLien, revertLien } from "./services/lien.js";
import { disburseLoan } from "./services/disbursal.js";
import { sendRejectionEmail } from "./services/email.js";
import type { LoanRequest, WorkflowResult } from "./types.js";

export const QUEUE_NAME = "loan-booking";

// Absurd uses DATABASE_URL (or PG* env vars) for its own connection pool.
// Our `pool` (from db.ts) is used only for application-level loans table queries.
export const absurd = new Absurd({
  db: process.env.DATABASE_URL ?? "postgresql://localhost/absurd",
  queueName: QUEUE_NAME,
});

/**
 * Loan booking durable workflow.
 *
 * Steps:
 *  1. insert-loan-request   – persist loan record in DB
 *  2. credit-check          – call credit bureau (mocked)
 *     └─ send-rejection-email (if failed)
 *  3. check-lender-product  – verify lender can fund the loan
 *  4. place-lien            – register lien on collateral (mocked)
 *  5. disburse-loan         – transfer funds (mocked)
 *     └─ revert-lien        – compensating step on disbursal failure
 */
absurd.registerTask<LoanRequest, WorkflowResult>(
  {
    name: "loan-booking-workflow",
    defaultMaxAttempts: 3,
  },
  async (params, ctx) => {
    // ── Step 1: Insert loan request ────────────────────────────────────────
    const loanRecord = await ctx.step("insert-loan-request", async () => {
      const result = await pool.query<{ id: string; created_at: string }>(
        `INSERT INTO loans (applicant_id, amount, purpose, collateral_id, task_id)
         VALUES ($1, $2, $3, $4, $5)
         ON CONFLICT (task_id) DO UPDATE SET task_id = EXCLUDED.task_id
         RETURNING id, created_at::text`,
        [
          params.applicant_id,
          params.amount,
          params.purpose,
          params.collateral_id,
          ctx.taskID,
        ],
      );
      const { id, created_at } = result.rows[0];
      console.log(`[${ctx.taskID}] Loan request inserted → loan_id=${id}`);
      return { loan_id: id, created_at };
    });

    // ── Step 2: Credit check ───────────────────────────────────────────────
    const creditResult = await ctx.step("credit-check", async () => {
      console.log(
        `[${ctx.taskID}] Running credit check for applicant=${params.applicant_id}`,
      );
      const result = await performCreditCheck(
        params.applicant_id,
        params.amount,
      );
      await pool.query(
        `UPDATE loans
         SET credit_score = $1,
             status       = $2
         WHERE id = $3`,
        [
          result.credit_score,
          result.approved ? "credit_check_passed" : "credit_check_failed",
          loanRecord.loan_id,
        ],
      );
      console.log(
        `[${ctx.taskID}] Credit check → score=${result.credit_score} approved=${result.approved}`,
      );
      return result;
    });

    // ── Step 3a: Rejection path ────────────────────────────────────────────
    if (!creditResult.approved) {
      await ctx.step("send-rejection-email", async () => {
        await sendRejectionEmail(
          params.applicant_id,
          creditResult.reason ?? "Credit check failed",
        );
        await pool.query(
          `UPDATE loans
           SET status           = 'credit_check_failed',
               rejection_reason = $1
           WHERE id = $2`,
          [creditResult.reason, loanRecord.loan_id],
        );
      });

      return {
        loan_id: loanRecord.loan_id,
        status: "credit_check_failed",
        credit_score: creditResult.credit_score,
        reason: creditResult.reason,
      };
    }

    // ── Step 3b: Lender product check ──────────────────────────────────────
    const lenderResult = await ctx.step("check-lender-product", async () => {
      console.log(
        `[${ctx.taskID}] Checking lender product for amount=${params.amount} purpose="${params.purpose}"`,
      );
      const result = await checkLenderProduct(params.amount, params.purpose);
      if (!result.can_fund) {
        await pool.query(
          `UPDATE loans
           SET status           = 'lender_check_failed',
               rejection_reason = $1
           WHERE id = $2`,
          [result.reason, loanRecord.loan_id],
        );
      }
      return result;
    });

    if (!lenderResult.can_fund) {
      return {
        loan_id: loanRecord.loan_id,
        status: "lender_check_failed",
        reason: lenderResult.reason,
      };
    }

    // ── Step 4: Place lien on collateral ───────────────────────────────────
    const lienResult = await ctx.step("place-lien", async () => {
      console.log(
        `[${ctx.taskID}] Placing lien on collateral=${params.collateral_id}`,
      );
      const result = await placeLien(
        loanRecord.loan_id,
        params.collateral_id,
        params.amount,
      );
      if (result.success) {
        await pool.query(
          `UPDATE loans
           SET status         = 'lien_placed',
               lien_reference = $1
           WHERE id = $2`,
          [result.lien_reference, loanRecord.loan_id],
        );
        console.log(
          `[${ctx.taskID}] Lien placed → reference=${result.lien_reference}`,
        );
      }
      return result;
    });

    if (!lienResult.success) {
      await pool.query(
        `UPDATE loans
         SET status           = 'disbursal_failed',
             rejection_reason = $1
         WHERE id = $2`,
        [lienResult.reason, loanRecord.loan_id],
      );
      return {
        loan_id: loanRecord.loan_id,
        status: "lien_failed",
        reason: lienResult.reason,
      };
    }

    // ── Step 5: Disburse loan ──────────────────────────────────────────────
    const disbursalResult = await ctx.step("disburse-loan", async () => {
      console.log(`[${ctx.taskID}] Disbursing loan=${loanRecord.loan_id}`);
      return await disburseLoan(
        loanRecord.loan_id,
        params.applicant_id,
        params.amount,
      );
    });

    // ── Step 5a: Revert lien on disbursal failure ──────────────────────────
    if (!disbursalResult.success) {
      await ctx.step("revert-lien", async () => {
        console.log(
          `[${ctx.taskID}] Disbursal failed – reverting lien=${lienResult.lien_reference}`,
        );
        await revertLien(lienResult.lien_reference!);
        await pool.query(
          `UPDATE loans
           SET status           = 'disbursal_failed',
               lien_reference   = NULL,
               rejection_reason = $1
           WHERE id = $2`,
          [disbursalResult.reason, loanRecord.loan_id],
        );
      });

      return {
        loan_id: loanRecord.loan_id,
        status: "disbursal_failed",
        reason: disbursalResult.reason,
      };
    }

    // ── Step 5b: Mark disbursed ────────────────────────────────────────────
    await ctx.step("mark-disbursed", async () => {
      await pool.query(
        `UPDATE loans
         SET status       = 'disbursed',
             disbursed_at = $1
         WHERE id = $2`,
        [disbursalResult.disbursed_at, loanRecord.loan_id],
      );
    });

    console.log(
      `[${ctx.taskID}] Loan disbursed → txn=${disbursalResult.transaction_id}`,
    );

    return {
      loan_id: loanRecord.loan_id,
      status: "disbursed",
      credit_score: creditResult.credit_score,
      interest_rate: lenderResult.interest_rate,
      transaction_id: disbursalResult.transaction_id,
      disbursed_at: disbursalResult.disbursed_at,
    };
  },
);
