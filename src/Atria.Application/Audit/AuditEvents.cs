namespace Atria.Application.Audit;

/// <summary>Stable entity-type names recorded in the audit journal.</summary>
public static class AuditEntities
{
    public const string Property = "Property";
    public const string Publication = "Publication";
    public const string SupportTicket = "SupportTicket";
    public const string User = "User";
    public const string CriticalAction = "CriticalAction";
    public const string RegulatoryReport = "RegulatoryReport";
}

/// <summary>
/// Stable event-type names recorded in the audit journal. These are part of the admin API contract —
/// the journal filters on them — so do not rename them casually.
/// </summary>
public static class AuditEvents
{
    public const string PropertyCreated = "PropertyCreated";
    public const string PropertyUpdated = "PropertyUpdated";
    public const string PropertyPublished = "PropertyPublished";
    public const string PropertyAnnounced = "PropertyAnnounced";
    public const string PublicationPublished = "PublicationPublished";
    public const string TicketOpened = "TicketOpened";
    public const string TicketClosed = "TicketClosed";
    public const string UserBanned = "UserBanned";
    public const string UserUnbanned = "UserUnbanned";
    public const string PasswordReset = "PasswordReset";
    public const string PasswordRestored = "PasswordRestored";
    public const string RealtorRegistered = "RealtorRegistered";
    public const string BanAppealSubmitted = "BanAppealSubmitted";
    public const string CriticalActionRequested = "CriticalActionRequested";
    public const string CriticalActionApproved = "CriticalActionApproved";
    public const string CriticalActionRejected = "CriticalActionRejected";
    public const string RegulatoryReportFiled = "RegulatoryReportFiled";
    public const string IssueTokensAnnulled = "IssueTokensAnnulled";
    public const string IssueInvalidated = "IssueInvalidated";
    public const string InvestmentWithdrawn = "InvestmentWithdrawn";
}
