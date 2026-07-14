using Atria.Application.Abstractions;
using Atria.Application.Deals.Queries;
using Atria.Domain.Deals;
using Atria.Domain.Factories;
using Atria.Domain.Investments;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Deals;

/// <summary>
/// Verifies GET /deals/me enriches successful deals with the matched investment amount, currency,
/// and the realtor's commission earnings (amount × percent), while pending deals carry no amount.
/// </summary>
public sealed class GetMyDealsCommissionTests
{
    private readonly IDealRepository _deals = Substitute.For<IDealRepository>();
    private readonly IInvestmentRepository _investments = Substitute.For<IInvestmentRepository>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IReferralLinkBuilder _links = Substitute.For<IReferralLinkBuilder>();

    private static readonly Guid RealtorId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);

    private GetMyDealsQueryHandler CreateSut() => new(_deals, _investments, _currentUser, _links);

    public GetMyDealsCommissionTests()
    {
        _currentUser.UserId.Returns(RealtorId);
        _links.BuildReferralUrl(Arg.Any<string>()).Returns("https://atria.app/invest?ref=x");
    }

    [Fact]
    public async Task Successful_deal_reports_investment_amount_currency_and_commission()
    {
        // 4.2% of 10 000 KGS = 420 KGS.
        var deal = Deal.Create(RealtorId, Guid.NewGuid(), 4.2m, Now);
        var investment = InvestmentFactory.CreateForInvestor(
            Guid.NewGuid(), deal.PropertyId, tokenCount: 100, amount: 10_000m, currency: "KGS");
        deal.MarkSuccessful(investment.Id);

        _deals.GetByRealtorAsync(RealtorId, Arg.Any<CancellationToken>()).Returns(new[] { deal });
        _investments.GetByIdsAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(investment.Id)),
                Arg.Any<CancellationToken>())
            .Returns(new[] { investment });

        var result = await CreateSut().Handle(new GetMyDealsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value.Single();
        dto.Status.Should().Be("successful");
        dto.InvestmentAmount.Should().Be(10_000m);
        dto.Currency.Should().Be("KGS");
        dto.CommissionAmount.Should().Be(420m);
    }

    [Fact]
    public async Task Pending_deal_carries_no_amount_or_commission()
    {
        var deal = Deal.Create(RealtorId, Guid.NewGuid(), 5m, Now);

        _deals.GetByRealtorAsync(RealtorId, Arg.Any<CancellationToken>()).Returns(new[] { deal });
        _investments.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Investment>());

        var result = await CreateSut().Handle(new GetMyDealsQuery(), CancellationToken.None);

        var dto = result.Value.Single();
        dto.Status.Should().Be("pending");
        dto.InvestmentAmount.Should().BeNull();
        dto.Currency.Should().BeNull();
        dto.CommissionAmount.Should().BeNull();
    }
}
