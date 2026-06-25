namespace Atria.Domain.Notifications;

/// <summary>Catalogue of notification templates. The body/subject lookup lives in the sender.</summary>
public enum NotificationTemplate
{
    OtpCode = 0,
    KycApproved = 1,
    KycRejected = 2,
    ApplicationSubmitted = 3,
    ApplicationApproved = 4,
    ApplicationRejected = 5,
    PaymentCompleted = 6,
    InvestmentActivated = 7
}
