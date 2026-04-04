# Loan Booking Example (.NET)

A .NET implementation of the loan booking durable workflow, mirroring the
[e2e-example](../../../../e2e-example) TypeScript version. It demonstrates
how to use the Absurd .NET SDK to build resilient, multi-step workflows with
automatic checkpointing and the embedded Habitat dashboard.

## Projects

| Project | Description |
|---------|-------------|
| `LoanBooking` | ASP.NET Core Web API — accepts loan applications, serves the Habitat dashboard at `/habitat` |
| `LoanBooking.Worker` | .NET Worker Service — standalone process that runs the durable workflow |

## Workflow Steps

```
loan-booking-workflow
  ├─ insert-loan-request     persist loan record in the loans table
  ├─ credit-check            deterministic mock (score ≥ 650 passes)
  │   └─ send-rejection-email  (on failure)
  ├─ check-lender-product    rejects amounts > $500,000
  ├─ place-lien              ~5% simulated failure rate
  └─ disburse-loan           ~10% simulated failure rate
      └─ revert-lien         compensating step on disbursal failure
```

Each step result is checkpointed in Postgres — if the worker crashes and the
task is retried, completed steps are replayed from cache without calling the
handler again.

## Prerequisites

- .NET 10 SDK
- PostgreSQL with the [Absurd schema](../../../../sql/absurd.sql) applied:
  ```bash
  psql -d absurd -f ../../../../sql/absurd.sql
  ```

## Configuration

Both projects read the connection string from `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "Absurd": "Host=localhost;Database=absurd;Username=postgres;Password=postgres"
  }
}
```

## Running

Open two terminals.

**Terminal 1 — Worker (processes loans):**
```bash
cd LoanBooking.Worker
dotnet run
```

**Terminal 2 — API (accepts loan requests):**
```bash
cd LoanBooking
dotnet run
```

The API starts on `https://localhost:5001` (or `http://localhost:5000`).

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/loans` | Submit a loan application |
| `GET` | `/loans/{id}` | Fetch the loan record from the DB |
| `GET` | `/tasks/{taskId}` | Poll the Absurd workflow task state |
| `GET` | `/habitat` | Embedded Habitat monitoring dashboard |

## Example Requests

**Submit a loan:**
```bash
curl -X POST http://localhost:5000/loans \
  -H "Content-Type: application/json" \
  -d '{
    "applicantId": "alice-123",
    "amount": 50000,
    "purpose": "Home renovation",
    "collateralId": "PROP-456"
  }'
```

Response:
```json
{
  "task_id": "019...",
  "run_id":  "019...",
  "message": "Loan booking workflow started"
}
```

**Poll task state:**
```bash
curl http://localhost:5000/tasks/<task_id>
```

**Get loan record:**
```bash
curl http://localhost:5000/loans/<loan_id>
```

**Open the dashboard:**
Navigate to `http://localhost:5000/habitat` to monitor tasks, runs, and checkpoints.
