using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Investments;
using Atria.Application.Investments.Commands;
using Atria.Domain.Investments;
using Atria.Domain.Kyc;
using Microsoft.Extensions.Options;
using NSubstitute;
using FluentAssertions;

namespace Atria.Application.Tests.Investments;

/// <summary>
/// Shares are divisible into hundredths, so a purchase is no longer rounded down to whole tokens:
/// an investor can put in less than one token's price, and what they get is exactly what their
/// money covers at the share granularity — never a sliver more, which would place more of the issue
/// than was paid for.
/// </summary>
public sealed class CreateInvestmentFractionalTests
{
    private readonly IInvestmentRepository _investments = Substitute.For<IInvestmentRepository>();
    private readonly IKycRepository _kyc = Substitute.For<IKycRepository>();
    private readonly IPropertyRepository _properties = Substitute.For<IPropertyRepository>();
    private readonly IDealRepository _deals = Substitute.For<IDealRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    private CreateInvestmentCommandHandler CreateSut() =>
        new(_investments, _kyc, _properties, _deals, _uow, _currentUser, _clock,
            Options.Create(new InvestmentReservationOptions()));

    private static KycProfile ApprovedKyc(Guid userId)
    {
        var kyc = KycProfile.Create(userId);
        kyc.Submit(KycProviderType.Manual, "session", null, null, null, null, null);
        kyc.Approve();
        return kyc;
    }

    /// <summary>An open property whose shares cost <paramref name="tokenPrice"/> each.</summary>
    private Property GivenOpenProperty(decimal tokenPrice, decimal totalTokens)
    {
        var investorId = Guid.NewGuid();
        _currentUser.UserId.Returns(investorId);
        _kyc.GetByUserIdAsync(investorId, Arg.Any<CancellationToken>()).Returns(ApprovedKyc(investorId));

        var property = Property.Create(
            "Borsan Residence, кв. 1", null, null, tokenPrice * totalTokens, tokenPrice, totalTokens, "KGS");
        property.Publish();
        _properties.GetByIdAsync(property.Id, Arg.Any<CancellationToken>()).Returns(property);
        return property;
    }

    private async Task<Investment> CapturedInvestmentAsync(Property property, decimal amount)
    {
        Investment? captured = null;
        await _investments.AddAsync(
            Arg.Do<Investment>(i => captured = i), Arg.Any<CancellationToken>());

        var result = await CreateSut().Handle(
            new CreateInvestmentCommand(property.Id, amount), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        captured.Should().NotBeNull();
        return captured!;
    }

    [Fact]
    public async Task Less_than_one_token_is_a_real_purchase()
    {
        // 8 000 сом за токен, инвестор кладёт 400 — раньше это была ошибка «amount_too_low»,
        // теперь это 0,05 доли.
        var property = GivenOpenProperty(tokenPrice: 8_000m, totalTokens: 10_000m);

        var investment = await CapturedInvestmentAsync(property, 400m);

        investment.TokenCount.Should().Be(0.05m);
        property.AvailableTokens.Should().Be(9_999.95m);
    }

    [Fact]
    public async Task The_smallest_possible_purchase_is_one_hundredth_of_a_token()
    {
        var property = GivenOpenProperty(tokenPrice: 1_000m, totalTokens: 100m);

        var investment = await CapturedInvestmentAsync(property, 10m);

        investment.TokenCount.Should().Be(TokenAmount.Smallest);
    }

    [Fact]
    public async Task An_amount_below_the_smallest_share_is_still_rejected()
    {
        var property = GivenOpenProperty(tokenPrice: 1_000m, totalTokens: 100m);

        var result = await CreateSut().Handle(
            new CreateInvestmentCommand(property.Id, 9m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("investment.amount_too_low");
        await _investments.DidNotReceive().AddAsync(Arg.Any<Investment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_non_terminating_division_rounds_down_never_up()
    {
        // 100 сом при цене 3 — 33,33… доли. Округление вверх разместило бы больше выпуска,
        // чем оплачено, поэтому дробь всегда отсекается вниз.
        var property = GivenOpenProperty(tokenPrice: 3m, totalTokens: 1_000m);

        var investment = await CapturedInvestmentAsync(property, 100m);

        investment.TokenCount.Should().Be(33.33m);
        property.AvailableTokens.Should().Be(1_000m - 33.33m);
    }

    [Fact]
    public async Task A_fractional_issue_can_be_bought_out_to_the_last_hundredth()
    {
        // Квартира 57,55 м² — выпуск ровно 57,55 доли; остаток обязан закрыться в ноль.
        var property = GivenOpenProperty(tokenPrice: 1_000m, totalTokens: 57.55m);

        await CapturedInvestmentAsync(property, 57_550m);

        property.AvailableTokens.Should().Be(0m);
    }
}
