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
    private static Property NewProperty(long totalTokens = 100, long minPurchaseTokens = 1)
        => Property.Create("Tower One", null, null, 1_000_000m, 100m, totalTokens, "KGS",
            minPurchaseTokens: minPurchaseTokens);

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
    public void An_issue_is_cut_into_whole_shares()
    {
        // 57,55 м² больше не значит 57,55 доли: токен — доля выпуска, а не квадратный метр, и
        // выпуск режется так, чтобы цена доли была мелкой, а количество — целым.
        var property = NewProperty(500_000);

        property.TotalTokens.Should().Be(500_000);
        property.AvailableTokens.Should().Be(500_000);
    }

    [Fact]
    public void Reservations_add_up_exactly()
    {
        var property = NewProperty(5_755);

        property.ReserveTokens(1);
        property.ReserveTokens(754);
        property.ReserveTokens(5_000);

        property.AvailableTokens.Should().Be(0, "целые доли должны закрывать выпуск точно, а не почти");
    }

    [Fact]
    public void A_reservation_still_cannot_oversubscribe()
    {
        var property = NewProperty(100);
        property.ReserveTokens(99);

        var act = () => property.ReserveTokens(2);

        act.Should().Throw<DomainException>().WithMessage("*more tokens than are available*");
    }

    [Fact]
    public void A_minimum_purchase_larger_than_the_issue_is_rejected()
    {
        var act = () => NewProperty(100, minPurchaseTokens: 101);

        act.Should().Throw<DomainException>().WithMessage("*cannot exceed the whole issue*");
    }

    [Fact]
    public void A_minimum_purchase_below_one_token_is_rejected()
    {
        var act = () => NewProperty(100, minPurchaseTokens: 0);

        act.Should().Throw<DomainException>().WithMessage("*at least 1 token*");
    }

    [Fact]
    public void The_minimum_purchase_carries_its_price()
    {
        var property = NewProperty(500_000, minPurchaseTokens: 100);

        property.MinPurchaseAmount.Should().Be(10_000m, "100 долей по 100 — это и есть порог входа");
    }
}
