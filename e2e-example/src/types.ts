export interface LoanRequest {
  applicant_id: string;
  amount: number;
  purpose: string;
  collateral_id: string;
}

export type LoanStatus =
  | "pending"
  | "credit_check_passed"
  | "credit_check_failed"
  | "lender_check_failed"
  | "lien_placed"
  | "disbursed"
  | "disbursal_failed";

export interface LoanRecord {
  id: string;
  applicant_id: string;
  amount: number;
  purpose: string;
  collateral_id: string;
  status: LoanStatus;
  credit_score?: number;
  rejection_reason?: string;
  lien_reference?: string;
  disbursed_at?: string;
  task_id?: string;
  created_at: string;
}

export interface WorkflowResult {
  loan_id: string;
  status: string;
  credit_score?: number;
  interest_rate?: number;
  transaction_id?: string;
  disbursed_at?: string;
  reason?: string;
}
