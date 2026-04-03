import "dotenv/config";
import express, { type Request, type Response } from "express";
import { pool, ensureLoansTable } from "./db.js";
import { absurd, QUEUE_NAME } from "./workflow.js";
import type { LoanRequest } from "./types.js";

const app = express();
app.use(express.json());

// ── POST /loans ────────────────────────────────────────────────────────────
// Submit a new loan application and start the durable workflow.
app.post("/loans", async (req: Request, res: Response) => {
  const { applicant_id, amount, purpose, collateral_id } =
    req.body as Partial<LoanRequest>;

  if (!applicant_id || !purpose || !collateral_id) {
    res.status(400).json({
      error: "applicant_id, amount, purpose, and collateral_id are required",
    });
    return;
  }

  const parsedAmount = Number(amount);
  if (!Number.isFinite(parsedAmount) || parsedAmount <= 0) {
    res.status(400).json({ error: "amount must be a positive number" });
    return;
  }

  try {
    const spawned = await absurd.spawn("loan-booking-workflow", {
      applicant_id,
      amount: parsedAmount,
      purpose,
      collateral_id,
    } satisfies LoanRequest);

    res.status(202).json({
      task_id: spawned.taskID,
      run_id: spawned.runID,
      message: "Loan booking workflow started",
    });
  } catch (err) {
    console.error("Failed to spawn loan workflow:", err);
    res.status(500).json({ error: "Failed to start loan workflow" });
  }
});

// ── GET /loans/:loan_id ────────────────────────────────────────────────────
// Fetch the loan record from the database.
app.get("/loans/:loan_id", async (req: Request, res: Response) => {
  try {
    const result = await pool.query(
      `SELECT id, applicant_id, amount, purpose, collateral_id,
              status, credit_score, rejection_reason,
              lien_reference, disbursed_at, task_id, created_at
       FROM loans
       WHERE id = $1`,
      [req.params.loan_id],
    );

    if (result.rows.length === 0) {
      res.status(404).json({ error: "Loan not found" });
      return;
    }

    res.json(result.rows[0]);
  } catch (err) {
    console.error("DB error:", err);
    res.status(500).json({ error: "Database error" });
  }
});

// ── GET /tasks/:task_id ────────────────────────────────────────────────────
// Poll the Absurd workflow task state (pending/running/sleeping/completed/failed).
app.get("/tasks/:task_id", async (req: Request, res: Response) => {
  try {
    const snapshot = await absurd.fetchTaskResult(req.params.task_id);
    if (snapshot === null) {
      res.status(404).json({ error: "Task not found" });
      return;
    }
    res.json(snapshot);
  } catch (err) {
    console.error("Absurd fetch error:", err);
    res.status(500).json({ error: "Failed to fetch task status" });
  }
});

// ── Startup ────────────────────────────────────────────────────────────────
const PORT = Number(process.env.PORT ?? 3000);

await ensureLoansTable();
await absurd.createQueue();

app.listen(PORT, () => {
  console.log(`Loan booking API  →  http://localhost:${PORT}`);
  console.log(`Queue: ${QUEUE_NAME}`);
  console.log("");
  console.log("Endpoints:");
  console.log(`  POST  /loans          – submit a loan application`);
  console.log(`  GET   /loans/:id      – get loan record from DB`);
  console.log(`  GET   /tasks/:task_id – poll workflow task state`);
});
