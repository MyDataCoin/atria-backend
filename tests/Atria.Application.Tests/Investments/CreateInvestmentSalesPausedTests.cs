using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Investments.Commands;
using Atria.Domain.Investments;
using Atria.Domain.Kyc;
using NSubstitute;
using FluentAssertions;

namespace Atria.Application.Tests.Investments;

/// <summary>
/// Verifies the purchase-block: an investor with approved KYC cannot invest in an OPEN property
/// whose sales are paused. The handler must short-circuit with a 409-style conflict
/// (<c>investment.sales_paused</c>) and never create an investment.
/// </summary>
public sealed class CreateInvestmentSalesPausedTests
{
    private readonly IInvestmentRepository _investments = Substitute.For<IInvestmentRepository>();
    private readonly IKycRepository _kyc = Substitute.For<IKycRepository>();
    private readonly IPropertyRepository _properties = Substitute.For<IPropertyRepository>();
    private readonly IDealRepository _deals = Substitute.For<IDealRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    private CreateInvestmentCommandHandler CreateSut() =>
        new(_investments, _kyc, _properties, _deals, _uow, _currentUser, _clock);

    private static KycProfile ApprovedKyc(Guid userId)
    {
        var kyc = KycProfile.Create(userId);
        kyc.Submit(KycProviderType.Manual, "session", null, null, null, null, null);
        kyc.Approve();
        return kyc;
    }

    [Fact]
    public async Task Investing_in_a_paused_open_property_is_rejected_and_creates_nothing()
    {
        var investorId = Guid.NewGuid();
        _currentUser.UserId.Returns(investorId);
        _kyc.GetByUserIdAsync(investorId, Arg.Any<CancellationToken>()).Returns(ApprovedKyc(investorId));

        var property = Property.Create("Tower One", null, null, 1_000_000m, 100m, 10_000, "USD");
        property.Publish();     // Open
        property.PauseSales();  // ...but frozen
        _properties.GetByIdAsync(property.Id, Arg.Any<CancellationToken>()).Returns(property);

        var result = await CreateSut().Handle(new CreateInvestmentCommand(property.Id, 1_000m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("investment.sales_paused");
        await _investments.DidNotReceive().AddAsync(Arg.Any<Investment>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
