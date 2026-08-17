using Atria.Domain.Common;
using Atria.Domain.Investments;
using FluentAssertions;

namespace Atria.Domain.Tests.Investments;

/// <summary>
/// Shares are divisible into hundredths, so an issue can be sized to something real (57.55 tokens
/// for a 57.55 m² apartment) and an investor can buy a part of one. These cover the granularity
/// itself: what is representable, how money rounds into shares, and the conversion the token
/// contract sees.
/// </summary>
public sealed class TokenAmountTests
{
    [Fact]
    public void Smallest_share_is_one_hundredth()
    {
        TokenAmount.Scale.Should().Be(2);
        TokenAmount.Smallest.Should().Be(0.01m);
    }

    [Theory]
    [InlineData(57.55)]
    [InlineData(0.01)]
    [InlineData(128.82)]
    [InlineData(1)]
    public void Amounts_at_the_granularity_are_well_formed(decimal tokens)
        => TokenAmount.IsWellFormed(tokens).Should().BeTrue();

    [Fact]
    public void An_amount_finer_than_the_granularity_is_rejected()
    {
        TokenAmount.IsWellFormed(0.001m).Should().BeFalse();

        var act = () => TokenAmount.ToMinor(0.001m);

        act.Should().Throw<DomainException>("silently rounding here would put the register and the chain on different numbers");
    }

    [Fact]
    public void Floor_never_rounds_a_holding_up()
    {
        // 100 сом при цене 3 сома — 33,33… доли. Инвестор получает ровно столько, сколько покрыли
        // деньги: округление вверх разместило бы больше выпуска, чем оплачено.
        TokenAmount.Floor(100m / 3m).Should().Be(33.33m);
        TokenAmount.Floor(0.999m).Should().Be(0.99m);
        TokenAmount.Floor(0.009m).Should().Be(0m);
    }

    [Fact]
    public void Normalize_rounds_to_the_nearest_share()
    {
        TokenAmount.Normalize(57.5504m).Should().Be(57.55m);
        TokenAmount.Normalize(0.005m).Should().Be(0.01m);
    }

    [Fact]
    public void Minor_units_round_trip_through_the_contract_representation()
    {
        // 57,55 доли контракт держит как 5755 минорных единиц (decimals() == Scale).
        TokenAmount.ToMinor(57.55m).Should().Be(5_755);
        TokenAmount.ToMinor(TokenAmount.Smallest).Should().Be(1);
        TokenAmount.FromMinor(5_755).Should().Be(57.55m);
        TokenAmount.FromMinor(TokenAmount.ToMinor(128.82m)).Should().Be(128.82m);
    }
}
