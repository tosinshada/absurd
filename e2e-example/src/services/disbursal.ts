export interface DisbursalResult {
  success: boolean;
  transaction_id?: string;
  disbursed_at?: string;
  reason?: string;
}

/**
 * Mock loan disbursal service.
 * In production this would initiate a fund transfer via a payment rail.
 * Simulates a ~10% failure rate.
 */
export async function disburseLoan(
  loan_id: string,
  _applicant_id: string,
  _amount: number,
): Promise<DisbursalResult> {
  await new Promise((resolve) => setTimeout(resolve, 200));

  if (Math.random() < 0.1) {
    return { success: false, reason: "Payment rail temporarily unavailable" };
  }

  return {
    success: true,
    transaction_id: `TXN-${loan_id.slice(0, 8).toUpperCase()}-${Date.now()}`,
    disbursed_at: new Date().toISOString(),
  };
}
