using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Investments;
using Atria.Application.Investments.Commands;
using Atria.Domain.Factories;
using Atria.Domain.Investments;
using Atria.Domain.Kyc;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Atria.Application.Tests.Investments;

/// <summary>
/// Covers the payment-free offering-application lifecycle: creating an application reserves tokens
/// from the pool up front, an operator approves it to activate (raising the activation event), and
/// rejecting/cancelling returns the reserved tokens.
/// </summary>
public sealed class InvestmentLifecycleTests
{
    private readonly IInvestmentRepository _investments = Substitute.For<IInvestmentRepository>();
    private readonly IKycRepository _kyc = Substitute.For<IKycRepository>();
    private readonly IPropertyRepository _properties = Substitute.For<IPropertyRepository>();
    private readonly IDealRepository _deals = Substitute.For<IDealRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    private static KycProfile ApprovedKyc(Guid userId)
    {
        var kyc = KycProfile.Create(userId);
        kyc.Submit(KycProviderType.Manual, "session", null, null, null, null, null);
        kyc.Approve();
        return kyc;
    }

    private static Property OpenProperty(long totalTokens = 100)
    {
        var p = Property.Create("Tower One", null, null, 1_000_000m, 100m, totalTokens, "USD");
        p.Publish();
        return p;
    }

    [Fact]
    public async Task Creating_an_application_reserves_tokens_and_persists()
    {
        var investorId = Guid.NewGuid();
        _currentUser.UserId.Returns(investorId);
        _clock.UtcNow.Returns(DateTime.UtcNow);
        _kyc.GetByUserIdAsync(investorId, Arg.Any<CancellationToken>()).Returns(ApprovedKyc(investorId));

        var property = OpenProperty(100);
        _properties.GetByIdAsync(property.Id, Arg.Any<CancellationToken>()).Returns(property);

        // 500 / 100 per token = 5 tokens.
        var result = await new CreateInvestmentCommandHandler(
                _investments, _kyc, _properties, _deals, _uow, _currentUser, _clock,
                Options.Create(new InvestmentReservationOptions()))
            .Handle(new CreateInvestmentCommand(property.Id, 500m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        property.AvailableTokens.Should().Be(95); // reserved 5 up front
        await _investments.Received(1).AddAsync(
            Arg.Is<Investment>(i => i.TokenCount == 5 && i.Status == InvestmentStatus.Reserved
                && i.PricePerToken == 100m), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Approving_a_reserved_application_activates_it()
    {
        var property = OpenProperty(100);
        var investment = InvestmentFactory.CreateForInvestor(
            Guid.NewGuid(), property.Id, 5, 500m, "USD", 100m, DateTime.UtcNow.AddDays(3));
        _investments.GetByIdAsync(investment.Id, Arg.Any<CancellationToken>()).Returns(investment);

        var result = await new ApproveInvestmentCommandHandler(_investments, _uow)
            .Handle(new ApproveInvestmentCommand(investment.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        investment.Status.Should().Be(InvestmentStatus.Active);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Approving_a_non_reserved_application_conflicts()
    {
        var investment = InvestmentFactory.CreateForInvestor(
            Guid.NewGuid(), Guid.NewGuid(), 5, 500m, "USD", 100m, DateTime.UtcNow.AddDays(3));
        investment.Approve(); // already Active
        _investments.GetByIdAsync(investment.Id, Arg.Any<CancellationToken>()).Returns(investment);

        var result = await new ApproveInvestmentCommandHandler(_investments, _uow)
            .Handle(new ApproveInvestmentCommand(investment.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task Rejecting_an_application_releases_the_reserved_tokens()
    {
        var property = OpenProperty(100);
        property.ReserveTokens(5); // as create would have
        property.AvailableTokens.Should().Be(95);

        var investment = InvestmentFactory.CreateForInvestor(
            Guid.NewGuid(), property.Id, 5, 500m, "USD", 100m, DateTime.UtcNow.AddDays(3));
        _investments.GetByIdAsync(investment.Id, Arg.Any<CancellationToken>()).Returns(investment);
        _properties.GetByIdAsync(property.Id, Arg.Any<CancellationToken>()).Returns(property);

        var result = await new RejectInvestmentCommandHandler(_investments, _properties, _uow)
            .Handle(new RejectInvestmentCommand(investment.Id, "does not meet policy"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        investment.Status.Should().Be(InvestmentStatus.Rejected);
        property.AvailableTokens.Should().Be(100); // returned to the pool
    }

    [Fact]
    public async Task Rejecting_without_a_reason_is_a_validation_error()
    {
        var result = await new RejectInvestmentCommandHandler(_investments, _properties, _uow)
            .Handle(new RejectInvestmentCommand(Guid.NewGuid(), "  "), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task Cancelling_own_application_releases_the_reserved_tokens()
    {
        var investorId = Guid.NewGuid();
        _currentUser.UserId.Returns(investorId);

        var property = OpenProperty(100);
        property.ReserveTokens(5);

        var investment = InvestmentFactory.CreateForInvestor(
            investorId, property.Id, 5, 500m, "USD", 100m, DateTime.UtcNow.AddDays(3));
        _investments.GetByIdAsync(investment.Id, Arg.Any<CancellationToken>()).Returns(investment);
        _properties.GetByIdAsync(property.Id, Arg.Any<CancellationToken>()).Returns(property);

        var result = await new CancelInvestmentCommandHandler(_investments, _properties, _uow, _currentUser)
            .Handle(new CancelInvestmentCommand(investment.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        investment.Status.Should().Be(InvestmentStatus.Cancelled);
        property.AvailableTokens.Should().Be(100);
    }

    [Fact]
    public void Expiring_a_reserved_application_releases_tokens_and_moves_to_expired()
    {
        // The domain transition the background reservation-expiry sweep drives: a lapsed reservation
        // returns its tokens to the pool and lands in the terminal Expired state (distinct from Cancelled).
        var property = OpenProperty(100);
        property.ReserveTokens(5);
        property.AvailableTokens.Should().Be(95);

        var investment = InvestmentFactory.CreateForInvestor(
            Guid.NewGuid(), property.Id, 5, 500m, "USD", 100m, DateTime.UtcNow.AddDays(-1));

        investment.Expire();
        property.ReleaseTokens(investment.TokenCount);

        investment.Status.Should().Be(InvestmentStatus.Expired);
        property.AvailableTokens.Should().Be(100);
    }

    [Fact]
    public async Task Cancelling_someone_elses_application_is_not_found()
    {
        _currentUser.UserId.Returns(Guid.NewGuid());
        var investment = InvestmentFactory.CreateForInvestor(
            Guid.NewGuid(), Guid.NewGuid(), 5, 500m, "USD", 100m, DateTime.UtcNow.AddDays(3));
        _investments.GetByIdAsync(investment.Id, Arg.Any<CancellationToken>()).Returns(investment);

        var result = await new CancelInvestmentCommandHandler(_investments, _properties, _uow, _currentUser)
            .Handle(new CancelInvestmentCommand(investment.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }
}
