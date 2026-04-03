import "dotenv/config";
import { ensureLoansTable } from "./db.js";
import { absurd, QUEUE_NAME } from "./workflow.js";

await ensureLoansTable();
await absurd.createQueue();

console.log(`Loan booking worker started – polling queue "${QUEUE_NAME}"`);

await absurd.startWorker({
  concurrency: 4,
  onError: (error) => {
    console.error("[worker] Unhandled error:", error);
  },
});
