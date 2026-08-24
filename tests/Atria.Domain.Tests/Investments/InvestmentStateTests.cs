using Atria.Domain.Common;
using Atria.Domain.Factories;
using Atria.Domain.Investments;
using Atria.Domain.Investments.Events;
using FluentAssertions;

namespace Atria.Domain.Tests.Investments;

public sealed class InvestmentStateTests
{
    private static Investment NewReservedInvestment()
        => InvestmentFactory.CreateForInvestor(
            Guid.NewGuid(), Guid.NewGuid(), 10, "KGS",
            pricePerToken: 100m, reservedUntilUtc: DateTime.UtcNow.AddDays(3));

    [Fact]
    public void Factory_CreateForInvestor_ProducesReservedAndCreatedEvent()
    {
        var investorId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();
        var reservedUntil = DateTime.UtcNow.AddDays(3);

        var investment = InvestmentFactory.CreateForInvestor(
            investorId, propertyId, 50, "KGS", pricePerToken: 100m, reservedUntilUtc: reservedUntil);

        investment.Status.Should().Be(InvestmentStatus.Reserved);
        investment.OnChainStatus.Should().Be(OnChainStatus.None);
        investment.TokenCount.Should().Be(50);
        investment.Amount.Should().Be(5000m);
        investment.Currency.Should().Be("KGS");
        investment.PricePerToken.Should().Be(100m);
        investment.ReservedUntilUtc.Should().Be(reservedUntil);
        var created = investment.DomainEvents.OfType<InvestmentCreatedEvent>().Single();
        created.InvestmentId.Should().Be(investment.Id);
        created.InvestorId.Should().Be(investorId);
        created.PropertyId.Should().Be(propertyId);
        created.Amount.Should().Be(5000m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Factory_WhenTokenCountNotPositive_ThrowsDomainException(long tokenCount)
    {
        var act = () => InvestmentFactory.CreateForInvestor(
            Guid.NewGuid(), Guid.NewGuid(), tokenCount, "KGS", 100m, DateTime.UtcNow.AddDays(3));

        act.Should().Throw<DomainException>().WithMessage("*Token count must be positive*");
    }

    [Fact]
    public void Factory_PricesTheApplicationFromTheWholeTokenCount()
    {
        // The amount is not an input any more. An investor who types 5 555 somoni at 100 a token
        // gets 55 tokens and owes 5 500 — the leftover 55 buys nothing, so charging it would charge
        // for shares that were never issued.
        var tokenCount = TokenAmount.FromMoney(5_555m, 100m);

        var investment = InvestmentFactory.CreateForInvestor(
            Guid.NewGuid(), Guid.NewGuid(), tokenCount, "KGS", 100m, DateTime.UtcNow.AddDays(3));

        investment.TokenCount.Should().Be(55);
        investment.Amount.Should().Be(5_500m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Factory_WhenCurrencyMissing_ThrowsDomainException(string currency)
    {
        var act = () => InvestmentFactory.CreateForInvestor(
            Guid.NewGuid(), Guid.NewGuid(), 10, currency, 100m, DateTime.UtcNow.AddDays(3));

        act.Should().Throw<DomainException>().WithMessage("*Currency is required*");
    }

    [Fact]
    public void Approve_FromReserved_ActivatesAndRaisesActivationEvent()
    {
        var investment = NewReservedInvestment();

        investment.Approve(DateTime.UtcNow);

        investment.Status.Should().Be(InvestmentStatus.Active);
        var activated = investment.DomainEvents.OfType<InvestmentActivatedEvent>().Single();
        activated.InvestmentId.Should().Be(investment.Id);
        activated.TokenCount.Should().Be(investment.TokenCount);
    }

    [Fact]
    public void Approve_WhenAlreadyActive_Throws()
    {
        var investment = NewReservedInvestment();
        investment.Approve(DateTime.UtcNow);

        var act = () => investment.Approve(DateTime.UtcNow);

        act.Should().Throw<InvalidStateTransitionException>();
        investment.Status.Should().Be(InvestmentStatus.Active);
    }

    [Fact]
    public void Reject_FromReserved_SetsRejectedAndRaisesRejectedEvent()
    {
        var investment = NewReservedInvestment();

        investment.Reject("does not meet policy");

        investment.Status.Should().Be(InvestmentStatus.Rejected);
        var rejected = investment.DomainEvents.OfType<InvestmentRejectedEvent>().Single();
        rejected.Reason.Should().Be("does not meet policy");
    }

    [Fact]
    public void Cancel_FromReserved_SetsCancelledAndRaisesCancelledEvent()
    {
        var investment = NewReservedInvestment();

        investment.Cancel();

        investment.Status.Should().Be(InvestmentStatus.Cancelled);
        investment.DomainEvents.Should().ContainSingle(e => e is InvestmentCancelledEvent);
    }

    [Fact]
    public void Reject_WhenAlreadyActive_Throws()
    {
        var investment = NewReservedInvestment();
        investment.Approve(DateTime.UtcNow);

        var act = () => investment.Reject("too late");

        act.Should().Throw<InvalidStateTransitionException>();
        investment.Status.Should().Be(InvestmentStatus.Active);
    }

    [Fact]
    public void Approve_WhenRejected_Throws()
    {
        var investment = NewReservedInvestment();
        investment.Reject("declined");

        var act = () => investment.Approve(DateTime.UtcNow);

        act.Should().Throw<InvalidStateTransitionException>();
        investment.Status.Should().Be(InvestmentStatus.Rejected);
    }
}
