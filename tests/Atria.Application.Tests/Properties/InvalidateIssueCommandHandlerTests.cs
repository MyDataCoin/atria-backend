using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Properties.Commands;
using Atria.Domain.Compliance;
using Atria.Domain.Holders;
using Atria.Domain.Investments;
using Atria.Domain.Refunds;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Properties;

/// <summary>
/// §73 does three things at once, and they must not come apart: the issue is marked invalid, the
/// shares are withdrawn from circulation, and what is owed back is recorded. Withdrawing without
/// recording would leave holders with neither shares nor a claim.
/// </summary>
public sealed class InvalidateIssueCommandHandlerTests
{
    private readonly IPropertyRepository _properties = Substitute.For<IPropertyRepository>();
    private readonly IHolderPositionRepository _positions = Substitute.For<IHolderPositionRepository>();
    private readonly IRefundObligationRepository _refunds = Substitute.For<IRefundObligationRepository>();
    private readonly IBlockchainOperationQueue _chain = Substitute.For<IBlockchainOperationQueue>();
    private readonly IAuditWriter _audit = Substitute.For<IAuditWriter>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid InvestorA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid InvestorB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string WalletA = "0xaaaa000000000000000000000000000000000000";
    private const string WalletB = "0xbbbb000000000000000000000000000000000000";

    private readonly List<RefundObligation> _raised = new();

    private InvalidateIssueCommandHandler NewHandler()
    {
        _refunds.When(r => r.AddAsync(Arg.Any<RefundObligation>(), Arg.Any<CancellationToken>()))
            .Do(c => _raised.Add(c.Arg<RefundObligation>()));
        return new InvalidateIssueCommandHandler(_properties, _positions, _refunds, _chain, _audit, _uow);
    }

    private Property GivenDeployedIssue()
    {
        var property = Property.Create("Tower One", null, null, 1_000_000m, 100m, 1_000, "KGS");
        property.Publish();
        property.SetTokenContract("0xcontract", "56", "0xissuer");
        _properties.GetByIdAsync(property.Id, Arg.Any<CancellationToken>()).Returns(property);
        return property;
    }

    private void GivenHolders(Guid propertyId, params (string Wallet, long Tokens, Guid? Investor)[] holders)
        => _positions.GetByPropertyAsync(propertyId, Arg.Any<CancellationToken>())
            .Returns(holders
                .Select(h => HolderPosition.Create(
                    propertyId, h.Wallet, h.Tokens, h.Investor, true, HolderSource.OurRecords, DateTime.UtcNow))
                .ToList());

    [Fact]
    public async Task Invalidating_marks_the_issue_stops_sales_and_records_what_is_owed()
    {
        var property = GivenDeployedIssue();
        GivenHolders(property.Id, (WalletA, 30, InvestorA), (WalletB, 10, InvestorB));

        var result = await NewHandler().Handle(
            new InvalidateIssueCommand(property.Id, "решение суда"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        property.Status.Should().Be(PropertyStatus.Invalidated);
        property.SalesPaused.Should().BeTrue();

        _raised.Should().HaveCount(2);
        // Refunded at the issue price: an invalid issue is unwound, not revalued.
        _raised.Single(r => r.InvestorId == InvestorA).Amount.Should().Be(3_000m);
        _raised.Single(r => r.InvestorId == InvestorB).Amount.Should().Be(1_000m);
        _raised.Should().OnlyContain(r => r.Status == RefundStatus.Pending
            && r.Reason == RefundReason.IssueInvalidated);

        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Every_holder_gets_a_burn_queued_against_their_address()
    {
        var property = GivenDeployedIssue();
        GivenHolders(property.Id, (WalletA, 30, InvestorA), (WalletB, 10, InvestorB));

        await NewHandler().Handle(
            new InvalidateIssueCommand(property.Id, "решение суда"), CancellationToken.None);

        await _chain.Received(1).EnqueueAsync(
            BlockchainOperationType.TokenBurn,
            Arg.Is<string>(p => p.Contains(WalletA)),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _chain.Received(1).EnqueueAsync(
            BlockchainOperationType.TokenBurn,
            Arg.Is<string>(p => p.Contains(WalletB)),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>An address with no investor behind it cannot be refunded; the shares still go.</summary>
    [Fact]
    public async Task An_unlinked_address_is_still_withdrawn_but_owes_nobody()
    {
        var property = GivenDeployedIssue();
        GivenHolders(property.Id, (WalletA, 30, InvestorA), (WalletB, 10, null));

        await NewHandler().Handle(
            new InvalidateIssueCommand(property.Id, "решение суда"), CancellationToken.None);

        _raised.Should().ContainSingle().Which.InvestorId.Should().Be(InvestorA);
        await _chain.Received(1).EnqueueAsync(
            BlockchainOperationType.TokenBurn,
            Arg.Is<string>(p => p.Contains(WalletB)),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Zero_balance_addresses_are_left_alone()
    {
        var property = GivenDeployedIssue();
        GivenHolders(property.Id, (WalletA, 30, InvestorA), (WalletB, 0, InvestorB));

        await NewHandler().Handle(
            new InvalidateIssueCommand(property.Id, "решение суда"), CancellationToken.None);

        _raised.Should().ContainSingle().Which.InvestorId.Should().Be(InvestorA);
    }

    [Fact]
    public async Task An_issue_with_no_contract_yet_records_refunds_without_queueing_burns()
    {
        var property = Property.Create("Tower One", null, null, 1_000_000m, 100m, 1_000, "KGS");
        property.Publish();
        _properties.GetByIdAsync(property.Id, Arg.Any<CancellationToken>()).Returns(property);
        GivenHolders(property.Id, (WalletA, 30, InvestorA));

        await NewHandler().Handle(
            new InvalidateIssueCommand(property.Id, "решение суда"), CancellationToken.None);

        _raised.Should().ContainSingle();
        await _chain.DidNotReceive().EnqueueAsync(
            Arg.Any<BlockchainOperationType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invalidating_without_a_reason_is_refused()
    {
        var property = GivenDeployedIssue();

        var result = await NewHandler().Handle(
            new InvalidateIssueCommand(property.Id, "  "), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        property.Status.Should().Be(PropertyStatus.Open);
    }

    [Fact]
    public async Task Invalidating_an_already_invalidated_issue_conflicts()
    {
        var property = GivenDeployedIssue();
        property.Invalidate();
        GivenHolders(property.Id);

        var result = await NewHandler().Handle(
            new InvalidateIssueCommand(property.Id, "решение суда"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }
}
