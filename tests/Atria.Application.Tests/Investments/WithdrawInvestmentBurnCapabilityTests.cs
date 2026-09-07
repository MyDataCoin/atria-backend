using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Application.Investments.Commands;
using Atria.Domain.Compliance;
using Atria.Domain.Factories;
using Atria.Domain.Investments;
using Atria.Domain.Refunds;
using Atria.Domain.Whitelist;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Investments;

/// <summary>
/// A withdrawal of shares that exist on chain is refused outright when the platform cannot burn them,
/// rather than accepted halfway.
/// </summary>
/// <remarks>
/// The compliance key is deliberately not required at start-up, so the process can be serving with no
/// ability to destroy shares. Before this check, a withdrawal in that state marked the investment
/// withdrawn, raised the refund obligation and only then failed inside the worker — the investor kept
/// both the money back and the shares, and the register disagreed with the chain until a
/// reconciliation caught it. Nothing must change unless the whole operation can complete.
/// </remarks>
public sealed class WithdrawInvestmentBurnCapabilityTests
{
    private static readonly DateTime Activated = new(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);
    private const string Wallet = "0x23E3C895cC4f77B85443feE0042Dc105BeB00B93";

    private readonly IInvestmentRepository _investments = Substitute.For<IInvestmentRepository>();
    private readonly IPropertyRepository _properties = Substitute.For<IPropertyRepository>();
    private readonly IHolderPositionRepository _positions = Substitute.For<IHolderPositionRepository>();
    private readonly IRefundObligationRepository _refunds = Substitute.For<IRefundObligationRepository>();
    private readonly IComplianceRepository _profiles = Substitute.For<IComplianceRepository>();
    private readonly IWhitelistEntryRepository _whitelist = Substitute.For<IWhitelistEntryRepository>();
    private readonly IBlockchainOperationQueue _chain = Substitute.For<IBlockchainOperationQueue>();
    private readonly IBurnCapability _burn = Substitute.For<IBurnCapability>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly IAuditWriter _audit = Substitute.For<IAuditWriter>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private WithdrawInvestmentCommandHandler CreateSut() =>
        new(_investments, _properties, _positions, _refunds, _profiles, _whitelist, _chain, _burn,
            _currentUser, _clock, _audit, _uow);

    /// <summary>An activated investment whose shares were minted to <see cref="Wallet"/>.</summary>
    private Investment ArrangeMintedInvestment()
    {
        var investorId = Guid.NewGuid();
        _currentUser.UserId.Returns(investorId);
        _clock.UtcNow.Returns(Activated.AddDays(3));

        var property = Property.Create("Borsan Residence", null, null, 1_000_000m, 100m, 10_000, "KGS");
        property.SetTokenContract(
            "0x440bc3b478d6d5c18ec537431e7C3e602E46c088", "bsc-testnet", Wallet);
        property.ReserveTokens(10); // the purchase being withdrawn from

        var investment = InvestmentFactory.CreateForInvestor(
            investorId, property.Id, 10, "KGS", 100m, Activated.AddDays(3));
        investment.Approve(Activated);
        investment.SetTokenTarget(Wallet, property.TokenContractAddress!);
        investment.SetOnChainResult("0xabc", OnChainStatus.Confirmed);

        _investments.GetByIdAsync(investment.Id, Arg.Any<CancellationToken>()).Returns(investment);
        _properties.GetByIdAsync(property.Id, Arg.Any<CancellationToken>()).Returns(property);

        return investment;
    }

    [Fact]
    public async Task Without_the_compliance_key_the_withdrawal_is_refused_and_nothing_changes()
    {
        _burn.CanBurn.Returns(false);
        var investment = ArrangeMintedInvestment();

        var result = await CreateSut().Handle(
            new WithdrawInvestmentCommand(investment.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("investment.burnUnavailable");

        // The three effects that must not happen halfway.
        investment.Status.Should().Be(InvestmentStatus.Active);
        await _refunds.DidNotReceive().AddAsync(Arg.Any<RefundObligation>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task With_the_compliance_key_the_withdrawal_proceeds_as_before()
    {
        _burn.CanBurn.Returns(true);
        var investment = ArrangeMintedInvestment();

        var result = await CreateSut().Handle(
            new WithdrawInvestmentCommand(investment.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        investment.Status.Should().Be(InvestmentStatus.Withdrawn);
        await _refunds.Received(1).AddAsync(Arg.Any<RefundObligation>(), Arg.Any<CancellationToken>());
        await _chain.Received(1).EnqueueAsync(
            BlockchainOperationType.TokenBurn, Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The capability is only asked about when it is actually needed. An application activated but
    /// never minted has nothing on chain to undo, and refusing that withdrawal would deny a §44 right
    /// over a key the operation does not use.
    /// </summary>
    [Fact]
    public async Task An_investment_that_never_reached_the_chain_withdraws_without_the_key()
    {
        _burn.CanBurn.Returns(false);

        var investorId = Guid.NewGuid();
        _currentUser.UserId.Returns(investorId);
        _clock.UtcNow.Returns(Activated.AddDays(3));

        var property = Property.Create("Borsan Residence", null, null, 1_000_000m, 100m, 10_000, "KGS");
        property.ReserveTokens(10);
        var investment = InvestmentFactory.CreateForInvestor(
            investorId, property.Id, 10, "KGS", 100m, Activated.AddDays(3));
        investment.Approve(Activated);

        _investments.GetByIdAsync(investment.Id, Arg.Any<CancellationToken>()).Returns(investment);
        _properties.GetByIdAsync(property.Id, Arg.Any<CancellationToken>()).Returns(property);

        var result = await CreateSut().Handle(
            new WithdrawInvestmentCommand(investment.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        investment.Status.Should().Be(InvestmentStatus.Withdrawn);
        await _chain.DidNotReceive().EnqueueAsync(
            Arg.Any<BlockchainOperationType>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }
}
