using Atria.Application.Abstractions;
using Atria.Application.Compliance.EventHandlers;
using Atria.Domain.Compliance;
using Atria.Domain.Investments;
using Atria.Domain.Investments.Events;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Atria.Application.Tests.Compliance;

/// <summary>
/// Order and target both matter here: the wallet has to be allowlisted on the issue's own network
/// before the mint, or the token contract reverts the allocation with the address still not
/// permitted.
/// </summary>
public sealed class AddToAllowlistOnInvestmentActivatedHandlerTests
{
    private readonly IComplianceRepository _profiles = Substitute.For<IComplianceRepository>();
    private readonly ITesseraComplianceService _tessera = Substitute.For<ITesseraComplianceService>();
    private readonly IBlockchainOperationQueue _queue = Substitute.For<IBlockchainOperationQueue>();
    private readonly IProcessedEventStore _processed = Substitute.For<IProcessedEventStore>();
    private readonly IPropertyRepository _properties = Substitute.For<IPropertyRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid InvestorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Wallet = "0xaaaa000000000000000000000000000000000000";

    public AddToAllowlistOnInvestmentActivatedHandlerTests()
        // Verification passes by default so the allowlist path is reachable; the case where it
        // refuses has its own test below.
        => _tessera.VerifyPresentationAsync(InvestorId, Arg.Any<CancellationToken>())
            .Returns(true);

    private AddToAllowlistOnInvestmentActivatedHandler NewHandler() =>
        new(_profiles, _tessera, _queue, _processed, _properties, _uow,
            NullLogger<AddToAllowlistOnInvestmentActivatedHandler>.Instance);

    private Property GivenIssueOn(string? chain)
    {
        var property = Property.Create("Tower One", null, null, 1_000_000m, 100m, 1_000, "KGS");
        property.Publish();
        if (chain is not null)
            property.SetTokenContract("0xcontract", chain, "0xissuer");

        _properties.GetByIdAsync(property.Id, Arg.Any<CancellationToken>()).Returns(property);
        return property;
    }

    private void GivenWallet(string? wallet)
    {
        var profile = ComplianceProfile.Create(InvestorId, wallet);
        _profiles.GetByInvestorAsync(InvestorId, Arg.Any<CancellationToken>()).Returns(profile);
    }

    private InvestmentActivatedEvent ActivationFor(Property property) =>
        new(Guid.NewGuid(), InvestorId, property.Id, 10, 1_000m);

    [Fact]
    public async Task The_wallet_is_allowlisted_on_the_issues_own_network()
    {
        var property = GivenIssueOn("bsc-testnet");
        GivenWallet(Wallet);

        await NewHandler().HandleAsync(ActivationFor(property), CancellationToken.None);

        await _tessera.Received(1).AddToAllowlistAsync("bsc-testnet", Wallet, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_allocation_is_queued_after_the_wallet_is_allowlisted()
    {
        var property = GivenIssueOn("bsc-testnet");
        GivenWallet(Wallet);

        await NewHandler().HandleAsync(ActivationFor(property), CancellationToken.None);

        Received.InOrder(() =>
        {
            _tessera.AddToAllowlistAsync("bsc-testnet", Wallet, Arg.Any<CancellationToken>());
            _queue.EnqueueAsync(
                BlockchainOperationType.TokenAllocation, Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>());
        });
    }

    /// <summary>
    /// Without a network there is no allowlist to write to. Guessing one would allowlist the wallet
    /// somewhere harmless and leave the mint reverting.
    /// </summary>
    [Fact]
    public async Task An_issue_with_no_network_recorded_does_nothing_on_chain()
    {
        var property = GivenIssueOn(null);
        GivenWallet(Wallet);

        await NewHandler().HandleAsync(ActivationFor(property), CancellationToken.None);

        await _tessera.DidNotReceive().AddToAllowlistAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _queue.DidNotReceive().EnqueueAsync(
            BlockchainOperationType.TokenAllocation, Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verification is what stands between an approved application and shares on chain, so its
    /// refusal has to stop both writes. This path had no test, which is how a verification that
    /// refused EVERY investor — an empty policy id compared against the configured one — went
    /// unnoticed until a run produced an approved application and an empty queue.
    /// </summary>
    [Fact]
    public async Task A_refused_verification_does_nothing_on_chain()
    {
        var property = GivenIssueOn("bsc-testnet");
        GivenWallet(Wallet);
        _tessera.VerifyPresentationAsync(InvestorId, Arg.Any<CancellationToken>()).Returns(false);

        await NewHandler().HandleAsync(ActivationFor(property), CancellationToken.None);

        await _tessera.DidNotReceive().AddToAllowlistAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _queue.DidNotReceive().EnqueueAsync(
            BlockchainOperationType.TokenAllocation, Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The allowlist write and the allocation are one decision: queueing the mint while the address
    /// is still not permitted leaves the contract to revert it.
    /// </summary>
    [Fact]
    public async Task A_verified_investor_gets_both_operations_queued()
    {
        var property = GivenIssueOn("bsc-testnet");
        GivenWallet(Wallet);

        await NewHandler().HandleAsync(ActivationFor(property), CancellationToken.None);

        await _tessera.Received(1).AddToAllowlistAsync(
            "bsc-testnet", Wallet, Arg.Any<CancellationToken>());
        await _queue.Received(1).EnqueueAsync(
            BlockchainOperationType.TokenAllocation, Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_investor_without_a_wallet_does_nothing_on_chain()
    {
        var property = GivenIssueOn("bsc-testnet");
        GivenWallet(null);

        await NewHandler().HandleAsync(ActivationFor(property), CancellationToken.None);

        await _tessera.DidNotReceive().AddToAllowlistAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_redelivered_activation_is_a_no_op()
    {
        var property = GivenIssueOn("bsc-testnet");
        GivenWallet(Wallet);
        _processed.IsProcessedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        await NewHandler().HandleAsync(ActivationFor(property), CancellationToken.None);

        await _tessera.DidNotReceive().AddToAllowlistAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
