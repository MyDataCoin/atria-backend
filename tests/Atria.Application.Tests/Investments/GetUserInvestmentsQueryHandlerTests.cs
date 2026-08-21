using Atria.Application.Abstractions;
using Atria.Application.Investments.Queries;
using Atria.Domain.Investments;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Investments;

/// <summary>
/// Coverage for <see cref="GetUserInvestmentsQueryHandler"/>: the admin/compliance investor card read.
/// The share is computed server-side as <c>tokens / totalTokens * 100</c>, and an investor with no
/// active holdings yields an empty list (a success, never a failure/404).
/// </summary>
public sealed class GetUserInvestmentsQueryHandlerTests
{
    private readonly IInvestmentRepository _investments = Substitute.For<IInvestmentRepository>();

    private GetUserInvestmentsQueryHandler CreateSut() => new(_investments);

    [Fact]
    public async Task Maps_holdings_and_computes_share_percent()
    {
        var investorId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();
        _investments.GetActiveHoldingsByInvestorAsync(investorId, Arg.Any<CancellationToken>())
            .Returns(new[] { (propertyId, "Villa Sol Nabiati", 120L, 12_000m, "USD", 5_000L) });

        var result = await CreateSut().Handle(new GetUserInvestmentsQuery(investorId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value.Should().ContainSingle().Subject;
        dto.PropertyId.Should().Be(propertyId);
        dto.PropertyName.Should().Be("Villa Sol Nabiati");
        dto.TokenCount.Should().Be(120);
        dto.Amount.Should().Be(12_000m);
        dto.Currency.Should().Be("USD");
        dto.SharePercent.Should().Be(2.4m); // 120 / 5000 * 100
        dto.Status.Should().Be(InvestmentStatus.Active);
    }

    [Fact]
    public async Task No_active_holdings_yields_empty_list_success()
    {
        var investorId = Guid.NewGuid();
        _investments.GetActiveHoldingsByInvestorAsync(investorId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<(Guid, string, long, decimal, string, long)>());

        var result = await CreateSut().Handle(new GetUserInvestmentsQuery(investorId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Zero_total_tokens_does_not_throw_and_reports_zero_share()
    {
        var investorId = Guid.NewGuid();
        _investments.GetActiveHoldingsByInvestorAsync(investorId, Arg.Any<CancellationToken>())
            .Returns(new[] { (Guid.NewGuid(), "Broken", 10L, 100m, "USD", 0L) });

        var result = await CreateSut().Handle(new GetUserInvestmentsQuery(investorId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Single().SharePercent.Should().Be(0m);
    }
}
