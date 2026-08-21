namespace Atria.Application.Audit;

/// <summary>Stable entity-type names recorded in the audit journal.</summary>
public static class AuditEntities
{
    public const string Property = "Property";
    public const string Building = "Building";
    public const string Publication = "Publication";
    public const string SupportTicket = "SupportTicket";
    public const string User = "User";
    public const string CriticalAction = "CriticalAction";
    public const string RegulatoryReport = "RegulatoryReport";
    public const string PayoutRun = "PayoutRun";
    public const string BlockchainOperation = "BlockchainOperation";
}

/// <summary>
/// Stable event-type names recorded in the audit journal. These are part of the admin API contract —
/// the journal filters on them — so do not rename them casually.
/// </summary>
public static class AuditEvents
{
    public const string BlockchainOperationRetried = "BlockchainOperationRetried";
    public const string BuildingCreated = "BuildingCreated";
    public const string BuildingUpdated = "BuildingUpdated";
    public const string BuildingDeleted = "BuildingDeleted";
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
    public const string AdminRegistered = "AdminRegistered";
    public const string PasswordChanged = "PasswordChanged";
    public const string BanAppealSubmitted = "BanAppealSubmitted";
    public const string CriticalActionRequested = "CriticalActionRequested";
    public const string CriticalActionApproved = "CriticalActionApproved";
    public const string CriticalActionRejected = "CriticalActionRejected";
    public const string RegulatoryReportFiled = "RegulatoryReportFiled";
    public const string IssueTokensAnnulled = "IssueTokensAnnulled";
    public const string IssueInvalidated = "IssueInvalidated";
    public const string InvestmentWithdrawn = "InvestmentWithdrawn";
    public const string InvestmentAnnulled = "InvestmentAnnulled";
    public const string PayoutRunCreated = "PayoutRunCreated";
    public const string PayoutRunApproved = "PayoutRunApproved";
    public const string PayoutRunCancelled = "PayoutRunCancelled";
    public const string PayoutRunCompleted = "PayoutRunCompleted";
    public const string PayoutItemSettled = "PayoutItemSettled";
    public const string PayoutItemFailed = "PayoutItemFailed";
}
