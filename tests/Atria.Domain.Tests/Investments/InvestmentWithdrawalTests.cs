using Atria.Domain.Common;
using Atria.Domain.Factories;
using Atria.Domain.Investments;
using Atria.Domain.Investments.Events;
using FluentAssertions;

namespace Atria.Domain.Tests.Investments;

/// <summary>
/// The 14-day right of withdrawal (draft Decree, §44). Calendar days, no reason required, no
/// penalty — and firmly closed once the window passes.
/// </summary>
public sealed class InvestmentWithdrawalTests
{
    private static readonly DateTime Activated = new(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);

    private static Investment ActiveInvestment()
    {
        var investment = InvestmentFactory.CreateForInvestor(
            Guid.NewGuid(), Guid.NewGuid(), 10, "KGS", 100m, Activated.AddDays(3));
        investment.Approve(Activated);
        return investment;
    }

    [Fact]
    public void Activation_opens_a_window_of_fourteen_calendar_days()
    {
        var investment = ActiveInvestment();

        investment.WithdrawalDeadlineUtc.Should().Be(Activated.AddDays(14));
    }

    [Fact]
    public void A_reserved_application_has_no_withdrawal_window_yet()
    {
        var investment = InvestmentFactory.CreateForInvestor(
            Guid.NewGuid(), Guid.NewGuid(), 10, "KGS", 100m, Activated.AddDays(3));

        investment.WithdrawalDeadlineUtc.Should().BeNull();
        investment.CanWithdrawAt(Activated).Should().BeFalse();
    }

    [Fact]
    public void The_holder_can_withdraw_inside_the_window()
    {
        var investment = ActiveInvestment();

        investment.CanWithdrawAt(Activated.AddDays(13)).Should().BeTrue();
        investment.Withdraw(Activated.AddDays(13));

        investment.Status.Should().Be(InvestmentStatus.Withdrawn);
    }

    [Fact]
    public void Withdrawing_on_the_last_day_still_counts()
    {
        var investment = ActiveInvestment();

        investment.Withdraw(investment.WithdrawalDeadlineUtc!.Value);

        investment.Status.Should().Be(InvestmentStatus.Withdrawn);
    }

    /// <summary>After the window the purchase is final — the shares are the holder's.</summary>
    [Fact]
    public void Once_the_window_closes_the_purchase_is_final()
    {
        var investment = ActiveInvestment();

        var act = () => investment.Withdraw(Activated.AddDays(14).AddSeconds(1));

        act.Should().Throw<DomainException>().WithMessage("*window*");
        investment.Status.Should().Be(InvestmentStatus.Active);
    }

    [Fact]
    public void Withdrawal_raises_the_event_the_refund_and_burn_hang_off()
    {
        var investment = ActiveInvestment();

        investment.Withdraw(Activated.AddDays(1));

        investment.DomainEvents.OfType<InvestmentWithdrawnEvent>().Should().ContainSingle()
            .Which.TokenCount.Should().Be(10);
    }

    [Fact]
    public void An_investment_cannot_be_withdrawn_twice()
    {
        var investment = ActiveInvestment();
        investment.Withdraw(Activated.AddDays(1));

        var act = () => investment.Withdraw(Activated.AddDays(2));

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Only_an_active_investment_can_be_withdrawn()
    {
        var reserved = InvestmentFactory.CreateForInvestor(
            Guid.NewGuid(), Guid.NewGuid(), 10, "KGS", 100m, Activated.AddDays(3));

        var act = () => reserved.Withdraw(Activated);

        act.Should().Throw<DomainException>();
    }
}
