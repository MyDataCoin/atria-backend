using Atria.Domain.Common;
using Atria.Domain.Investments;
using FluentAssertions;

namespace Atria.Domain.Tests.Investments;

/// <summary>
/// A token is one indivisible share of an issue, so every count is a whole number. These cover the
/// three places that matters: turning money into shares, pricing the shares back, and the
/// conversion the token contract sees.
/// </summary>
public sealed class TokenAmountTests
{
    [Fact]
    public void A_share_does_not_divide()
    {
        TokenAmount.Scale.Should().Be(0, "the token contract's decimals() is zero");
        TokenAmount.Smallest.Should().Be(1);
    }

    [Fact]
    public void Money_never_rounds_up_into_a_share()
    {
        // 8 750 сомони при цене 87 — 100,57 доли. Инвестор получает 100: округление вверх
        // разместило бы больше выпуска, чем оплачено.
        TokenAmount.FromMoney(8_750m, 87m).Should().Be(100);
        TokenAmount.FromMoney(86.99m, 87m).Should().Be(0);
        TokenAmount.FromMoney(87m, 87m).Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_sum_buys_nothing(decimal amount)
        => TokenAmount.FromMoney(amount, 87m).Should().Be(0);

    [Fact]
    public void A_zero_price_is_refused_rather_than_dividing_by_it()
    {
        var act = () => TokenAmount.FromMoney(1_000m, 0m);

        act.Should().Throw<DomainException>().WithMessage("*price must be positive*");
    }

    [Fact]
    public void Shares_are_priced_back_exactly()
    {
        // Ровно то, что инвестор платит: количество × цена, без остатка.
        TokenAmount.CostOf(100, 87m).Should().Be(8_700m);
        TokenAmount.CostOf(TokenAmount.FromMoney(8_750m, 87m), 87m).Should().Be(8_700m);
    }

    [Fact]
    public void Minor_units_round_trip_through_the_contract_representation()
    {
        // decimals() == Scale == 0, поэтому доля и есть единица контракта.
        TokenAmount.ToMinor(5_755).Should().Be(5_755);
        TokenAmount.ToMinor(TokenAmount.Smallest).Should().Be(1);
        TokenAmount.FromMinor(5_755).Should().Be(5_755);
    }

    [Fact]
    public void A_negative_or_oversized_chain_reading_is_refused()
    {
        var negative = () => TokenAmount.ToMinor(-1);
        negative.Should().Throw<DomainException>();

        var tooLarge = () => TokenAmount.FromMinor(System.Numerics.BigInteger.Pow(2, 70));
        tooLarge.Should().Throw<DomainException>(
            "a balance that cannot be a share count means the contract is not the one we think it is");
    }
}
