export interface LenderCheckResult {
  can_fund: boolean;
  product_id?: string;
  interest_rate?: number;
  reason?: string;
}

/**
 * Mock lender product check.
 * Verifies the lender has a suitable product for the requested amount and purpose.
 * Loans above $500,000 exceed lender capacity.
 */
export async function checkLenderProduct(
  amount: number,
  purpose: string,
): Promise<LenderCheckResult> {
  await new Promise((resolve) => setTimeout(resolve, 80));

  if (amount > 500_000) {
    return {
      can_fund: false,
      reason: "Requested amount exceeds lender capacity ($500,000 limit)",
    };
  }

  const prefix = purpose.toUpperCase().replace(/\s+/g, "-").slice(0, 6);
  return {
    can_fund: true,
    product_id: `PROD-${prefix}-001`,
    interest_rate: amount > 100_000 ? 8.5 : 7.5,
  };
}
