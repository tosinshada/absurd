import { Pool } from "pg";

export const pool = new Pool({
  connectionString: process.env.DATABASE_URL ?? "postgresql://localhost/absurd",
});

export async function ensureLoansTable(): Promise<void> {
  await pool.query(`
    CREATE TABLE IF NOT EXISTS loans (
      id             UUID           PRIMARY KEY DEFAULT gen_random_uuid(),
      applicant_id   TEXT           NOT NULL,
      amount         NUMERIC(15, 2) NOT NULL,
      purpose        TEXT           NOT NULL,
      collateral_id  TEXT           NOT NULL,
      status         TEXT           NOT NULL DEFAULT 'pending',
      credit_score   INTEGER,
      rejection_reason TEXT,
      lien_reference TEXT,
      disbursed_at   TIMESTAMPTZ,
      task_id        TEXT           UNIQUE,
      created_at     TIMESTAMPTZ    NOT NULL DEFAULT NOW()
    )
  `);
}
