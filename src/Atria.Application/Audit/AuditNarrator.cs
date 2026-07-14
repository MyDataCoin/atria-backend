using System.Globalization;
using Atria.Domain.Audit;
using Atria.Domain.Common;
using Atria.Domain.Compliance.Events;
using Atria.Domain.Deals.Events;
using Atria.Domain.Investments.Events;
using Atria.Domain.Support.Events;

namespace Atria.Application.Audit;

/// <summary>
/// Turns a domain event into the journal's display copy: a Russian summary, a clean action name
/// (no <c>Event</c> suffix) and a severity. Used by the background audit handler for events that are
/// not audited explicitly inside a command — without this the journal shows a raw C# class name and
/// an empty details column.
///
/// Events with no narration are NOT journalled at all (see <see cref="ShouldAudit"/>): the admin
/// journal is an operations log, not a dump of every internal event.
/// </summary>
public static class AuditNarrator
{
    /// <summary>Whether this event belongs in the admin journal at all.</summary>
    public static bool ShouldAudit(IDomainEvent e) => Describe(e) is not null;

    /// <summary>The aggregate the event concerns, as shown/filtered in the journal.</summary>
    public static string EntityType(IDomainEvent e) => e switch
    {
        DealCreatedEvent or DealSucceededEvent or DealRejectedEvent => "Deal",
        InvestmentCreatedEvent or InvestmentActivatedEvent
            or PaymentCompletedEvent or PaymentFailedEvent => "Investment",
        TicketRepliedBySupportEvent => AuditEntities.SupportTicket,
        AllowlistUpdatedEvent or AttestationsRevokedEvent or DidIssuedEvent => "Compliance",
        _ => ActionName(e)
    };

    /// <summary>The action name shown in the journal (the event name without its <c>Event</c> suffix).</summary>
    public static string ActionName(IDomainEvent e)
    {
        var name = e.GetType().Name;
        const string suffix = "Event";
        return name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length
            ? name[..^suffix.Length]
            : name;
    }

    /// <summary>Russian summary + severity for the event, or null when it should not be journalled.</summary>
    public static (string Summary, AuditSeverity Severity)? Describe(IDomainEvent e) => e switch
    {
        // --- Realtor referral deals ---
        DealCreatedEvent d =>
            ($"Создана реферальная сделка (комиссия {Percent(d.CommissionPercent)}%)", AuditSeverity.Success),
        DealSucceededEvent d =>
            ($"Реферальная сделка завершена: по ссылке прошла инвестиция (комиссия {Percent(d.CommissionPercent)}%)",
                AuditSeverity.Success),
        DealRejectedEvent =>
            ("Реферальная сделка отклонена: ссылка истекла неиспользованной", AuditSeverity.Warning),

        // --- Investments & payments ---
        InvestmentCreatedEvent i =>
            ($"Создана заявка на инвестицию на сумму {Money(i.Amount)}", AuditSeverity.Success),
        InvestmentActivatedEvent i =>
            ($"Инвестиция активирована: {Money(i.Amount)}, токенов — {i.TokenCount}", AuditSeverity.Success),
        PaymentCompletedEvent p =>
            ($"Платёж получен на сумму {Money(p.Amount)}", AuditSeverity.Success),
        PaymentFailedEvent p =>
            ($"Платёж не прошёл: {p.Reason}", AuditSeverity.Alert),

        // --- Support ---
        TicketRepliedBySupportEvent t =>
            ($"Поддержка ответила на тикет «{t.Subject}»", AuditSeverity.Success),

        // --- Compliance / web3 ---
        AllowlistUpdatedEvent a => a.Added
            ? ($"Кошелёк {a.WalletAddress} добавлен в белый список", AuditSeverity.Success)
            : ($"Кошелёк {a.WalletAddress} удалён из белого списка", AuditSeverity.Warning),
        AttestationsRevokedEvent a =>
            ($"Отозваны аттестации инвестора: {a.Reason}", AuditSeverity.Alert),
        DidIssuedEvent d =>
            ($"Выпущен DID инвестора: {d.Did}", AuditSeverity.Success),

        // Everything else (KYC and any future internal event) stays out of the admin journal.
        _ => null
    };

    private static string Money(decimal amount)
        => amount.ToString("0.##", CultureInfo.InvariantCulture) + " KGS";

    private static string Percent(decimal percent)
        => percent.ToString("0.##", CultureInfo.InvariantCulture);
}
