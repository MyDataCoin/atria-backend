using Atria.Application.Abstractions;
using Atria.Application.Investments.Queries;
using Atria.Domain.Investments;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Investments;

/// <summary>
/// The quote is what the investor sees before confirming. Its job is to be the same arithmetic the
/// purchase then performs — a quote that promises a count the command declines to give would be
/// worse than no quote at all.
/// </summary>
public sealed class QuoteInvestmentQueryHandlerTests
{
    private readonly IPropertyRepository _properties = Substitute.For<IPropertyRepository>();

    private QuoteInvestmentQueryHandler NewHandler() => new(_properties);

    private Property GivenOffering(
        decimal tokenPrice = 87m, long totalTokens = 100_000, long minPurchaseTokens = 100,
        decimal? areaSqM = null)
    {
        var property = Property.Create(
            "Borsan Residence, кв. 1", null, null, tokenPrice * totalTokens, tokenPrice, totalTokens,
            "TJS", minPurchaseTokens: minPurchaseTokens);

        if (areaSqM is { } area)
            property.SetUnitDetails(totalAreaSqM: area);

        property.Publish();
        _properties.GetByIdAsync(property.Id, Arg.Any<CancellationToken>()).Returns(property);
        return property;
    }

    [Fact]
    public async Task The_leftover_of_an_entered_sum_is_reported_rather_than_charged()
    {
        var property = GivenOffering();

        var result = await NewHandler().Handle(
            new QuoteInvestmentQuery(property.Id, 8_750m), CancellationToken.None);

        var quote = result.Value;
        quote.TokenCount.Should().Be(100);
        quote.TotalAmount.Should().Be(8_700m);
        quote.ChangeAmount.Should().Be(50m, "остаток должен быть виден до подтверждения, а не списан");
        quote.RequestedAmount.Should().Be(8_750m);
        quote.CanPurchase.Should().BeTrue();
    }

    [Fact]
    public async Task A_sum_landing_exactly_on_a_token_boundary_leaves_no_change()
    {
        var property = GivenOffering();

        var quote = (await NewHandler().Handle(
            new QuoteInvestmentQuery(property.Id, 8_700m), CancellationToken.None)).Value;

        quote.TokenCount.Should().Be(100);
        quote.ChangeAmount.Should().Be(0m);
    }

    [Fact]
    public async Task A_sum_below_the_minimum_still_quotes_and_says_what_the_minimum_costs()
    {
        // Отказ отдаётся как расчёт, а не как ошибка: форме нужно показать, сколько внести,
        // а не только то, что введённого мало.
        var property = GivenOffering();

        var quote = (await NewHandler().Handle(
            new QuoteInvestmentQuery(property.Id, 5_000m), CancellationToken.None)).Value;

        quote.CanPurchase.Should().BeFalse();
        quote.MinPurchaseTokens.Should().Be(100);
        quote.MinPurchaseAmount.Should().Be(8_700m);
    }

    [Fact]
    public async Task A_sum_beyond_the_remaining_supply_cannot_be_purchased()
    {
        var property = GivenOffering(tokenPrice: 100m, totalTokens: 10, minPurchaseTokens: 1);

        var quote = (await NewHandler().Handle(
            new QuoteInvestmentQuery(property.Id, 1_100m), CancellationToken.None)).Value;

        quote.TokenCount.Should().Be(11);
        quote.AvailableTokens.Should().Be(10);
        quote.CanPurchase.Should().BeFalse();
    }

    [Fact]
    public async Task Area_and_share_come_back_as_equivalents_of_the_quoted_count()
    {
        // 57,55 м² на 100 000 долей: площадь — производная от количества, а не единица выпуска.
        var property = GivenOffering(areaSqM: 57.55m);

        var quote = (await NewHandler().Handle(
            new QuoteInvestmentQuery(property.Id, 8_750m), CancellationToken.None)).Value;

        quote.SharePercent.Should().Be(0.1m);
        quote.AreaEquivalentSqM.Should().BeApproximately(0.05755m, 0.000001m);
    }

    [Fact]
    public async Task An_offering_with_no_area_quotes_a_null_equivalent_rather_than_zero()
    {
        var property = GivenOffering();

        var quote = (await NewHandler().Handle(
            new QuoteInvestmentQuery(property.Id, 8_750m), CancellationToken.None)).Value;

        quote.AreaEquivalentSqM.Should().BeNull("нулевая площадь читалась бы как измеренная");
    }

    [Fact]
    public async Task Quoting_an_unknown_offering_is_a_not_found()
    {
        _properties.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Property?)null);

        var result = await NewHandler().Handle(
            new QuoteInvestmentQuery(Guid.NewGuid(), 1_000m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("investment.property_unavailable");
    }
}
