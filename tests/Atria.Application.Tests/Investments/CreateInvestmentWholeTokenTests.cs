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
/// A token is indivisible, so an application is always for a whole number of them. The sum the
/// investor types is turned into whole tokens (rounded down, never up — that would place more of the
/// issue than was paid for) and the application is then priced back off that count, so nobody is
/// charged for a fraction of a share that was never issued.
/// </summary>
public sealed class CreateInvestmentWholeTokenTests
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
    private Property GivenOpenProperty(decimal tokenPrice, long totalTokens, long minPurchaseTokens = 1)
    {
        var investorId = Guid.NewGuid();
        _currentUser.UserId.Returns(investorId);
        _kyc.GetByUserIdAsync(investorId, Arg.Any<CancellationToken>()).Returns(ApprovedKyc(investorId));

        var property = Property.Create(
            "Borsan Residence, кв. 1", null, null, tokenPrice * totalTokens, tokenPrice, totalTokens, "KGS",
            minPurchaseTokens: minPurchaseTokens);
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
    public async Task An_entered_sum_becomes_whole_tokens_and_is_charged_at_that_count()
    {
        // Цена доли 87 сомони, инвестор вводит 8 750 — это 100,57 доли. Он получает 100 и платит
        // 8 700: остаток 50 не покупает ничего, и списывать его было бы платой за невыпущенное.
        var property = GivenOpenProperty(tokenPrice: 87m, totalTokens: 100_000);

        var investment = await CapturedInvestmentAsync(property, 8_750m);

        investment.TokenCount.Should().Be(100);
        investment.Amount.Should().Be(8_700m);
        property.AvailableTokens.Should().Be(99_900);
    }

    [Fact]
    public async Task A_sum_under_the_minimum_purchase_is_refused_with_the_minimum_in_the_message()
    {
        var property = GivenOpenProperty(tokenPrice: 87m, totalTokens: 100_000, minPurchaseTokens: 100);

        var result = await CreateSut().Handle(
            new CreateInvestmentCommand(property.Id, 8_600m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("investment.below_minimum");
        result.Error.Message.Should().Contain("100").And.Contain("8700");
        property.AvailableTokens.Should().Be(100_000, "отказ не должен резервировать доли");
        await _investments.DidNotReceive().AddAsync(Arg.Any<Investment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_sum_that_buys_nothing_at_all_is_refused()
    {
        var property = GivenOpenProperty(tokenPrice: 1_000m, totalTokens: 100);

        var result = await CreateSut().Handle(
            new CreateInvestmentCommand(property.Id, 999m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("investment.below_minimum");
        await _investments.DidNotReceive().AddAsync(Arg.Any<Investment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_non_terminating_division_rounds_down_never_up()
    {
        // 100 сомони при цене 3 — 33,33… доли. Округление вверх разместило бы больше выпуска,
        // чем оплачено, поэтому дробь всегда отсекается вниз.
        var property = GivenOpenProperty(tokenPrice: 3m, totalTokens: 1_000);

        var investment = await CapturedInvestmentAsync(property, 100m);

        investment.TokenCount.Should().Be(33);
        investment.Amount.Should().Be(99m);
        property.AvailableTokens.Should().Be(967);
    }

    [Fact]
    public async Task An_issue_can_be_bought_out_to_the_last_token()
    {
        var property = GivenOpenProperty(tokenPrice: 100m, totalTokens: 5_755);

        await CapturedInvestmentAsync(property, 575_500m);

        property.AvailableTokens.Should().Be(0);
    }

    [Fact]
    public async Task A_sum_beyond_the_remaining_capacity_is_refused()
    {
        var property = GivenOpenProperty(tokenPrice: 100m, totalTokens: 10);

        var result = await CreateSut().Handle(
            new CreateInvestmentCommand(property.Id, 1_100m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("investment.insufficient_tokens");
        property.AvailableTokens.Should().Be(10);
    }
}
