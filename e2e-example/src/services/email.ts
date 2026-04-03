/**
 * Mock email notification service.
 * In production this would send via an email provider (SendGrid, SES, etc.).
 */
export async function sendRejectionEmail(
  applicant_id: string,
  reason: string,
): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 60));
  console.log(
    `[email] Rejection email sent → applicant=${applicant_id} reason="${reason}"`,
  );
}
