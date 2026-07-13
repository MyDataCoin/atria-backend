using Atria.Domain.Common;
using Atria.Domain.Deals;
using Atria.Domain.Deals.Events;
using FluentAssertions;

namespace Atria.Domain.Tests.Deals;

public sealed class DealTests
{
    private static readonly DateTime Now = new(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);

    private static Deal NewDeal(decimal percent = 5m)
        => Deal.Create(Guid.NewGuid(), Guid.NewGuid(), percent, Now);

    [Fact]
    public void Create_StartsPending_WithLiveLink_AndRaisesCreatedEvent()
    {
        var realtorId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();

        var deal = Deal.Create(realtorId, propertyId, 7.5m, Now);

        deal.Status.Should().Be(DealStatus.Pending);
        deal.RealtorId.Should().Be(realtorId);
        deal.PropertyId.Should().Be(propertyId);
        deal.CommissionPercent.Should().Be(7.5m);
        deal.ReferralToken.Should().NotBeNullOrWhiteSpace();
        deal.ExpiresAtUtc.Should().Be(Now.Add(Deal.LinkLifetime));
        deal.MatchedInvestmentId.Should().BeNull();
        deal.IsRedeemable(Now).Should().BeTrue();
        deal.DomainEvents.Should().ContainSingle(e => e is DealCreatedEvent);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Create_RejectsPercentOutOfRange(decimal percent)
    {
        var act = () => Deal.Create(Guid.NewGuid(), Guid.NewGuid(), percent, Now);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ReferralTokens_AreUnique()
    {
        var a = NewDeal();
        var b = NewDeal();
        a.ReferralToken.Should().NotBe(b.ReferralToken);
    }

    [Fact]
    public void IsRedeemable_IsFalse_AfterExpiry()
    {
        var deal = NewDeal();
        deal.IsRedeemable(deal.ExpiresAtUtc).Should().BeFalse();
        deal.IsRedeemable(deal.ExpiresAtUtc.AddSeconds(1)).Should().BeFalse();
    }

    [Fact]
    public void MarkSuccessful_MovesToSuccessful_AndRaisesEvent()
    {
        var deal = NewDeal();
        var investmentId = Guid.NewGuid();

        deal.MarkSuccessful(investmentId);

        deal.Status.Should().Be(DealStatus.Successful);
        deal.MatchedInvestmentId.Should().Be(investmentId);
        deal.DomainEvents.Should().ContainSingle(e => e is DealSucceededEvent);
    }

    [Fact]
    public void MarkSuccessful_IsNoOp_WhenAlreadySettled()
    {
        var deal = NewDeal();
        deal.Reject();

        deal.MarkSuccessful(Guid.NewGuid());

        deal.Status.Should().Be(DealStatus.Rejected);
        deal.MatchedInvestmentId.Should().BeNull();
    }

    [Fact]
    public void Reject_MovesPendingToRejected_AndIsIdempotent()
    {
        var deal = NewDeal();

        deal.Reject();
        deal.Reject();

        deal.Status.Should().Be(DealStatus.Rejected);
    }

    [Fact]
    public void Reject_IsNoOp_WhenSuccessful()
    {
        var deal = NewDeal();
        deal.MarkSuccessful(Guid.NewGuid());

        deal.Reject();

        deal.Status.Should().Be(DealStatus.Successful);
    }
}
