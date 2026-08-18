using Atria.Domain.Common;
using Atria.Domain.Factories;
using Atria.Domain.Investments;
using Atria.Domain.Investments.Events;
using FluentAssertions;

namespace Atria.Domain.Tests.Investments;

/// <summary>
/// Annulment is the operator's correction: a mistaken, duplicated or leftover application is voided and
/// its shares go back to the issue. It is the one way out of <see cref="InvestmentStatus.Active"/>
/// outside the statutory withdrawal window, and it exists so such a row is never deleted by hand —
/// a hand-deleted row does not give its shares back.
/// </summary>
public sealed class InvestmentAnnulmentTests
{
    private static Investment NewReserved()
        => InvestmentFactory.CreateForInvestor(
            Guid.NewGuid(), Guid.NewGuid(), 10, 1000m, "KGS",
            pricePerToken: 100m, reservedUntilUtc: DateTime.UtcNow.AddDays(3));

    private static Investment NewActive()
    {
        var investment = NewReserved();
        investment.Approve(DateTime.UtcNow);
        return investment;
    }

    [Fact]
    public void Reserved_can_be_annulled_and_reports_that_nothing_was_placed()
    {
        var investment = NewReserved();

        investment.Annul("Дубликат заявки");

        investment.Status.Should().Be(InvestmentStatus.Annulled);
        investment.RejectionReason.Should().Be("Дубликат заявки");
        var raised = investment.DomainEvents.OfType<InvestmentAnnulledEvent>().Single();
        raised.Reason.Should().Be("Дубликат заявки");
        raised.TokenCount.Should().Be(10);
        // Nothing was placed, so nothing has to be unwound downstream.
        raised.WasActive.Should().BeFalse();
    }

    [Fact]
    public void Active_can_be_annulled_long_after_the_withdrawal_window_closed()
    {
        var investment = NewActive();

        // Well past the 14 days — the point of the transition is that it is not time-limited.
        investment.Annul("Тестовые данные");

        investment.Status.Should().Be(InvestmentStatus.Annulled);
        investment.DomainEvents.OfType<InvestmentAnnulledEvent>().Single()
            .WasActive.Should().BeTrue();
    }

    [Fact]
    public void Annulment_without_a_reason_is_refused()
    {
        var investment = NewActive();

        var act = () => investment.Annul("   ");

        act.Should().Throw<DomainException>();
        investment.Status.Should().Be(InvestmentStatus.Active);
    }

    [Fact]
    public void An_annulled_application_cannot_be_annulled_again()
    {
        var investment = NewActive();
        investment.Annul("Ошибка оператора");

        var act = () => investment.Annul("Ещё раз");

        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Theory]
    [InlineData(InvestmentStatus.Rejected)]
    [InlineData(InvestmentStatus.Cancelled)]
    [InlineData(InvestmentStatus.Expired)]
    public void An_already_closed_application_cannot_be_annulled(InvestmentStatus closed)
    {
        var investment = NewReserved();
        switch (closed)
        {
            case InvestmentStatus.Rejected: investment.Reject("нет"); break;
            case InvestmentStatus.Cancelled: investment.Cancel(); break;
            case InvestmentStatus.Expired: investment.Expire(); break;
        }

        var act = () => investment.Annul("Уборка");

        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void An_annulled_application_is_closed_to_every_other_transition()
    {
        var investment = NewActive();
        investment.Annul("Тестовые данные");

        investment.Invoking(i => i.Approve(DateTime.UtcNow)).Should().Throw<InvalidStateTransitionException>();
        investment.Invoking(i => i.Cancel()).Should().Throw<InvalidStateTransitionException>();
        investment.Invoking(i => i.Withdraw(DateTime.UtcNow)).Should().Throw<DomainException>();
    }
}
