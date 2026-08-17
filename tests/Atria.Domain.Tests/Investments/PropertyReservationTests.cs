using Atria.Domain.Common;
using Atria.Domain.Investments;
using FluentAssertions;

namespace Atria.Domain.Tests.Investments;

/// <summary>
/// Covers the token-pool reservation semantics on <see cref="Property"/>: reserving at application
/// time claims capacity, and releasing returns it. This is what prevents oversubscription now that
/// there is no payment step.
/// </summary>
public sealed class PropertyReservationTests
{
    private static Property NewProperty(decimal totalTokens = 100)
        => Property.Create("Tower One", null, null, 1_000_000m, 100m, totalTokens, "USD");

    [Fact]
    public void ReserveTokens_reduces_available_supply()
    {
        var property = NewProperty(100);

        property.ReserveTokens(30);

        property.AvailableTokens.Should().Be(70);
        property.TotalTokens.Should().Be(100);
    }

    [Fact]
    public void ReserveTokens_more_than_available_throws()
    {
        var property = NewProperty(10);

        var act = () => property.ReserveTokens(11);

        act.Should().Throw<DomainException>().WithMessage("*more tokens than are available*");
        property.AvailableTokens.Should().Be(10);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ReserveTokens_non_positive_throws(long count)
    {
        var act = () => NewProperty().ReserveTokens(count);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ReleaseTokens_returns_reserved_supply()
    {
        var property = NewProperty(100);
        property.ReserveTokens(40);

        property.ReleaseTokens(40);

        property.AvailableTokens.Should().Be(100);
    }

    [Fact]
    public void ReleaseTokens_cannot_exceed_total_supply()
    {
        var property = NewProperty(100); // nothing reserved

        var act = () => property.ReleaseTokens(1);

        act.Should().Throw<DomainException>().WithMessage("*more tokens than the total supply*");
    }

    [Fact]
    public void Reserve_then_release_leaves_supply_unchanged()
    {
        var property = NewProperty(100);

        property.ReserveTokens(25);
        property.ReserveTokens(25);
        property.ReleaseTokens(25);

        property.AvailableTokens.Should().Be(75);
    }

    [Fact]
    public void An_issue_can_be_sized_to_a_fraction()
    {
        // 57,55 доли под квартиру в 57,55 м² — ровно то, ради чего доля стала делимой.
        var property = NewProperty(57.55m);

        property.TotalTokens.Should().Be(57.55m);
        property.AvailableTokens.Should().Be(57.55m);
    }

    [Fact]
    public void Fractional_reservations_add_up_exactly()
    {
        var property = NewProperty(57.55m);

        property.ReserveTokens(0.01m);
        property.ReserveTokens(7.54m);
        property.ReserveTokens(50m);

        property.AvailableTokens.Should().Be(0m, "the fractions must close the issue exactly, not nearly");
    }

    [Fact]
    public void A_fractional_reservation_still_cannot_oversubscribe()
    {
        var property = NewProperty(1m);
        property.ReserveTokens(0.99m);

        var act = () => property.ReserveTokens(0.02m);

        act.Should().Throw<DomainException>().WithMessage("*more tokens than are available*");
    }

    [Fact]
    public void An_issue_finer_than_the_share_granularity_is_rejected()
    {
        var act = () => NewProperty(57.555m);

        act.Should().Throw<DomainException>().WithMessage("*multiple of*");
    }

    [Fact]
    public void A_reservation_finer_than_the_share_granularity_is_rejected()
    {
        var property = NewProperty(100);

        var act = () => property.ReserveTokens(0.001m);

        act.Should().Throw<DomainException>().WithMessage("*multiple of*");
    }
}
