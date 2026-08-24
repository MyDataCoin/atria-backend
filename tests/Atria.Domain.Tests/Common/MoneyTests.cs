using Atria.Domain.Common;
using Atria.Domain.Holders;
using Atria.Domain.Investments;
using Atria.Domain.Payouts;
using FluentAssertions;

namespace Atria.Domain.Tests.Common;

public sealed class MoneyTests
{
    [Theory]
    [InlineData("KGS")]
    [InlineData("kgs")]
    [InlineData(" Kgs ")]
    public void Require_AcceptsTheSomHoweverItIsWritten(string currency)
        => Money.Require(currency).Should().Be("KGS");

    [Theory]
    [InlineData("TJS")]  // Tajikistani somoni — the code that reached the demo data
    [InlineData("USD")]
    [InlineData("RUB")]
    public void Require_RefusesAnyOtherCurrency(string currency)
    {
        var act = () => Money.Require(currency);

        act.Should().Throw<DomainException>().WithMessage("*KGS*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Require_WhenMissing_Throws(string? currency)
    {
        var act = () => Money.Require(currency);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Property_CannotBeIssuedInAnotherCurrency()
    {
        var act = () => Property.Create("Borsan Residence, кв. 1", null, null, 7_260_000m, 100m, 72_600, "TJS");

        act.Should().Throw<DomainException>().WithMessage("*KGS*");
    }

    [Fact]
    public void Property_StoresTheSomNormalised()
    {
        var property = Property.Create("Borsan Residence, кв. 1", null, null, 7_260_000m, 100m, 72_600, "kgs");

        property.Currency.Should().Be("KGS");
    }

    [Fact]
    public void PayoutRun_CannotBeDeclaredInAnotherCurrency()
    {
        // The distribution currency arrives straight from the request body rather than from the
        // issue, so it is the one that can drift away from what the shares were priced in.
        var operatorId = Guid.NewGuid();
        var snapshot = HolderSnapshot.Create(
            Guid.NewGuid(), DateTime.UtcNow, SnapshotPurpose.Payout, 1_000, operatorId,
            [new HolderSnapshotEntry("0xaaa", 1_000, Guid.NewGuid())]);

        var act = () => PayoutRun.Create(
            snapshot, PayoutKind.Dividend, PayoutMethod.BankTransfer,
            declaredAmount: 1_000m, currency: "USD", createdByUserId: operatorId, note: null);

        act.Should().Throw<DomainException>().WithMessage("*KGS*");
    }
}
