using Atria.Application.Audit;
using Atria.Domain.Audit;
using Atria.Domain.Compliance.Events;
using Atria.Domain.Deals.Events;
using Atria.Domain.Investments.Events;
using Atria.Domain.Kyc.Events;
using Atria.Domain.Support.Events;
using FluentAssertions;

namespace Atria.Application.Tests.Audit;

/// <summary>
/// The journal must read in Russian: every audited event gets a human summary, a clean action name
/// (no <c>Event</c> suffix) and a severity. Events with no narration (KYC, internal plumbing) stay
/// out of the admin journal entirely rather than showing a raw class name with empty details.
/// </summary>
public sealed class AuditNarratorTests
{
    [Fact]
    public void Deal_events_are_described_in_russian()
    {
        var created = new DealCreatedEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 4.2m);

        var (summary, severity) = AuditNarrator.Describe(created)!.Value;

        summary.Should().Be("Создана реферальная сделка (комиссия 4.2%)");
        severity.Should().Be(AuditSeverity.Success);
        AuditNarrator.ActionName(created).Should().Be("DealCreated", "the Event suffix must be stripped");
        AuditNarrator.EntityType(created).Should().Be("Deal");
    }

    [Fact]
    public void An_expired_referral_link_is_a_warning()
    {
        var rejected = new DealRejectedEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5m);

        var (summary, severity) = AuditNarrator.Describe(rejected)!.Value;

        summary.Should().Contain("истекла");
        severity.Should().Be(AuditSeverity.Warning);
    }

    [Fact]
    public void A_rejected_application_is_a_warning_and_names_the_reason()
    {
        var rejected = new InvestmentRejectedEvent(Guid.NewGuid(), Guid.NewGuid(), "Не соответствует политике");

        var (summary, severity) = AuditNarrator.Describe(rejected)!.Value;

        summary.Should().Be("Заявка на инвестицию отклонена: Не соответствует политике");
        severity.Should().Be(AuditSeverity.Warning);
    }

    [Fact]
    public void Investment_activation_names_the_amount_and_tokens()
    {
        var activated = new InvestmentActivatedEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TokenCount: 100, Amount: 10_000m);

        var (summary, _) = AuditNarrator.Describe(activated)!.Value;

        summary.Should().Be("Заявка одобрена, инвестиция активирована: 10000 KGS, токенов — 100");
        AuditNarrator.EntityType(activated).Should().Be("Investment");
    }

    [Fact]
    public void A_support_reply_is_described()
    {
        var replied = new TicketRepliedBySupportEvent(Guid.NewGuid(), Guid.NewGuid(), "Не проходит оплата");

        var (summary, _) = AuditNarrator.Describe(replied)!.Value;

        summary.Should().Be("Поддержка ответила на тикет «Не проходит оплата»");
    }

    [Fact]
    public void Allowlist_removal_is_a_warning_addition_is_not()
    {
        var added = new AllowlistUpdatedEvent(Guid.NewGuid(), "0xabc", Added: true);
        var removed = new AllowlistUpdatedEvent(Guid.NewGuid(), "0xabc", Added: false);

        AuditNarrator.Describe(added)!.Value.Severity.Should().Be(AuditSeverity.Success);
        AuditNarrator.Describe(removed)!.Value.Severity.Should().Be(AuditSeverity.Warning);
        AuditNarrator.Describe(removed)!.Value.Summary.Should().Contain("удалён из белого списка");
    }

    [Fact]
    public void Kyc_events_are_not_journalled()
    {
        var approved = new KycApprovedEvent(Guid.NewGuid(), Guid.NewGuid(), "0xabc");

        AuditNarrator.Describe(approved).Should().BeNull();
        AuditNarrator.ShouldAudit(approved).Should().BeFalse();
    }
}
