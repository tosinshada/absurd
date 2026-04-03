export interface LienResult {
  success: boolean;
  lien_reference?: string;
  reason?: string;
}

/**
 * Mock lien placement service.
 * In production this would register the lien with a collateral registry.
 * Simulates a ~5% failure rate.
 */
export async function placeLien(
  loan_id: string,
  collateral_id: string,
  _amount: number,
): Promise<LienResult> {
  await new Promise((resolve) => setTimeout(resolve, 150));

  if (Math.random() < 0.05) {
    return { success: false, reason: "Collateral registry temporarily unavailable" };
  }

  return {
    success: true,
    lien_reference: `LIEN-${collateral_id.toUpperCase()}-${loan_id.slice(0, 8).toUpperCase()}`,
  };
}

/**
 * Mock lien reversion service.
 * Releases a previously placed lien on failed disbursal.
 */
export async function revertLien(lien_reference: string): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 100));
  console.log(`[lien] Lien ${lien_reference} successfully reverted`);
}
