export interface CreditCheckResult {
  approved: boolean;
  credit_score: number;
  reason?: string;
}

/**
 * Mock credit check service.
 * Generates a deterministic score from the applicant_id so results are
 * consistent across retries.  Score >= 650 passes, < 650 fails.
 */
export async function performCreditCheck(
  applicant_id: string,
  _amount: number,
): Promise<CreditCheckResult> {
  await new Promise((resolve) => setTimeout(resolve, 100));

  const score = deterministicScore(applicant_id);
  const approved = score >= 650;

  return {
    approved,
    credit_score: score,
    reason: approved ? undefined : "Credit score below minimum threshold (650)",
  };
}

function deterministicScore(applicant_id: string): number {
  let hash = 0;
  for (let i = 0; i < applicant_id.length; i++) {
    hash = (hash * 31 + applicant_id.charCodeAt(i)) & 0xffffffff;
  }
  // Map to 500–800 range
  return 500 + (Math.abs(hash) % 301);
}
