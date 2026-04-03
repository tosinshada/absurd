# Loan Booking Workflow Example

A production-style example of a durable loan booking process built with **Express** and **Absurd** (Postgres-native durable workflows).

## Workflow

The following steps execute as durable checkpoints. If the process crashes and restarts, it resumes from the last completed checkpoint — no work is duplicated.

```
POST /loans
     │
     ▼
[1] insert-loan-request       ← persists the loan record in Postgres
     │
     ▼
[2] credit-check               ← calls credit bureau (mocked)
     │
  approved?
  ├─ NO  → [3a] send-rejection-email  → status: credit_check_failed
  └─ YES ─────────────────────────────────────────────────────────────┐
                                                                      │
[3b] check-lender-product      ← verifies lender has a matching product
     │
  can fund?
  ├─ NO  → status: lender_check_failed
  └─ YES ──────────────────────────────────────────────────────────────┐
                                                                       │
[4] place-lien                 ← registers lien on collateral (mocked)
     │
  success?
  ├─ NO  → status: lien_failed
  └─ YES ───────────────────────────────────────────────────────────────┐
                                                                        │
[5] disburse-loan              ← transfers funds (mocked)
     │
  success?
  ├─ NO  → [5a] revert-lien   ← compensating step (mocked)
  │              → status: disbursal_failed
  └─ YES → [5b] mark-disbursed → status: disbursed ✓
```

## Prerequisites

- Node.js 22+
- PostgreSQL (local or via `DATABASE_URL`)
- Absurd schema installed in your database:
  ```bash
  psql -d your_database -f ../sql/absurd.sql
  ```
  Or using `absurdctl`:
  ```bash
  absurdctl init --database your_database
  ```

## Setup

```bash
cd e2e-example
npm install

# Create a .env file from the template
cp .env.example .env
# Edit .env to point at your Postgres instance
```

## Running

Open two terminals:

**Terminal 1 – API server:**
```bash
npm run dev:server
```

**Terminal 2 – Workflow worker:**
```bash
npm run dev:worker
```

## Testing with curl

### Submit a loan application
```bash
curl -s -X POST http://localhost:3000/loans \
  -H 'Content-Type: application/json' \
  -d '{
    "applicant_id": "alice",
    "amount": 25000,
    "purpose": "home improvement",
    "collateral_id": "PROP-12345"
  }' | jq
```

Response:
```json
{
  "task_id": "...",
  "run_id": "...",
  "message": "Loan booking workflow started"
}
```

### Check the loan record in DB
```bash
curl -s http://localhost:3000/loans/<loan_id> | jq
```

### Poll the workflow task state
```bash
curl -s http://localhost:3000/tasks/<task_id> | jq
```

Response when complete:
```json
{
  "state": "completed",
  "result": {
    "loan_id": "...",
    "status": "disbursed",
    "credit_score": 712,
    "interest_rate": 7.5,
    "transaction_id": "TXN-...",
    "disbursed_at": "2026-04-03T..."
  }
}
```

### Try a rejected applicant
Applicant IDs that hash to a score < 650 are automatically rejected by the mock credit check. Try `"applicant_id": "z"` for a rejection path.

```bash
curl -s -X POST http://localhost:3000/loans \
  -H 'Content-Type: application/json' \
  -d '{
    "applicant_id": "z",
    "amount": 10000,
    "purpose": "car loan",
    "collateral_id": "VIN-98765"
  }' | jq
```

### Try a loan that exceeds lender capacity (> $500,000)
```bash
curl -s -X POST http://localhost:3000/loans \
  -H 'Content-Type: application/json' \
  -d '{
    "applicant_id": "bob",
    "amount": 750000,
    "purpose": "commercial real estate",
    "collateral_id": "BLDG-001"
  }' | jq
```

## Durable execution guarantees

Each `ctx.step(...)` call is a checkpoint. Absurd stores the result in Postgres before continuing. On worker restart or transient failure:

- Completed steps are **skipped** — their cached result is returned immediately
- The workflow resumes at the first incomplete step
- The `insert-loan-request` step uses `ON CONFLICT (task_id) DO UPDATE` to be safe against the rare case where the INSERT succeeds but the checkpoint write doesn't

## Project Structure

```
e2e-example/
├── src/
│   ├── types.ts               – shared TypeScript types
│   ├── db.ts                  – Postgres pool + loans table setup
│   ├── workflow.ts            – Absurd task registration
│   ├── server.ts              – Express API server
│   ├── worker.ts              – Absurd worker process
│   └── services/
│       ├── credit-check.ts    – mock credit bureau
│       ├── lender.ts          – mock lender product check
│       ├── lien.ts            – mock lien placement / reversion
│       ├── disbursal.ts       – mock fund transfer
│       └── email.ts           – mock email notifications
├── package.json
├── tsconfig.json
└── .env.example
```
