namespace LoanBooking.Worker.Services;

/// <summary>
/// Mock email notification service.
/// In production this would send via an email provider (SendGrid, SES, etc.).
/// </summary>
public static class EmailService
{
    public static void SendRejection(string applicantId, string reason, ILogger logger)
    {
        logger.LogInformation(
            "[email] Rejection email sent → applicant={ApplicantId} reason=\"{Reason}\"",
            applicantId, reason);
    }
}
