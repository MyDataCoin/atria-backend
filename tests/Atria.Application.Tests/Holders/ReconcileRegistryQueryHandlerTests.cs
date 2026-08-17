using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Holders.Queries;
using Atria.Domain.Holders;
using Atria.Domain.Investments;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Holders;

/// <summary>
/// The three checks that decide whether the registry can be relied on. Each catches a different way
/// the books and the chain drift apart, and none of them may pass by default.
/// </summary>
public sealed class ReconcileRegistryQueryHandlerTests
{
    private readonly IPropertyRepository _properties = Substitute.For<IPropertyRepository>();
    private readonly IHolderPositionRepository _positions = Substitute.For<IHolderPositionRepository>();
    private readonly ITokenReader _tokens = Substitute.For<ITokenReader>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    private static readonly DateTime Now = new(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);
    private const string WalletA = "0xaaaa000000000000000000000000000000000000";
    private const string WalletB = "0xbbbb000000000000000000000000000000000000";

    private readonly Property _property = Property.Create("Tower One", null, null, 1_000_000m, 100m, 1_000, "KGS");

    public ReconcileRegistryQueryHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _property.Publish();
        _property.SetTokenContract("0xcontract", "bsc-testnet", "0xissuer");
        _properties.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_property);
    }

    private ReconcileRegistryQueryHandler NewHandler() =>
        new(_properties, _positions, _tokens, _clock);

    private void GivenHolders(params (string Wallet, decimal Tokens, bool Allowlisted)[] holders)
        => _positions.GetByPropertyAsync(_property.Id, Arg.Any<CancellationToken>())
            .Returns(holders
                .Select(h => HolderPosition.Create(
                    _property.Id, h.Wallet, h.Tokens, Guid.NewGuid(), h.Allowlisted,
                    HolderSource.Chain, Now))
                .ToList());

    private void GivenChainSupply(long supply)
        => _tokens.GetTotalSupplyAsync("bsc-testnet", "0xcontract", Arg.Any<CancellationToken>())
            .Returns(supply);

    /// <summary>Places <paramref name="count"/> shares so the issue's own books show them as sold.</summary>
    private void GivenPlaced(long count) => _property.ReserveTokens(count);

    [Fact]
    public async Task A_registry_that_agrees_with_the_chain_is_clean()
    {
        GivenHolders((WalletA, 700, true), (WalletB, 300, true));
        GivenPlaced(1_000);
        GivenChainSupply(1_000);

        var result = await NewHandler().Handle(new ReconcileRegistryQuery(_property.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsClean.Should().BeTrue();
        result.Value.RegistryTotal.Should().Be(1_000);
        result.Value.ChainTotalSupply.Should().Be(1_000);
    }

    [Fact]
    public async Task Shares_the_chain_knows_about_but_the_registry_does_not_are_reported()
    {
        GivenHolders((WalletA, 700, true));
        GivenPlaced(700);
        GivenChainSupply(1_000);

        var result = await NewHandler().Handle(new ReconcileRegistryQuery(_property.Id), CancellationToken.None);

        result.Value.Discrepancies.Should().Contain(d => d.Code == "registry.supplyMismatch");
    }

    [Fact]
    public async Task Books_that_disagree_with_themselves_are_reported()
    {
        GivenHolders((WalletA, 700, true));
        GivenPlaced(900); // the issue thinks 900 were placed
        GivenChainSupply(700);

        var result = await NewHandler().Handle(new ReconcileRegistryQuery(_property.Id), CancellationToken.None);

        result.Value.Discrepancies.Should().Contain(d => d.Code == "registry.soldMismatch");
    }

    /// <summary>The one a regulator asks about: shares on an address compliance never approved.</summary>
    [Fact]
    public async Task A_holder_who_is_not_on_the_allowlist_is_reported()
    {
        GivenHolders((WalletA, 700, true), (WalletB, 300, false));
        GivenPlaced(1_000);
        GivenChainSupply(1_000);

        var result = await NewHandler().Handle(new ReconcileRegistryQuery(_property.Id), CancellationToken.None);

        result.Value.Discrepancies.Should().Contain(d =>
            d.Code == "registry.holderNotAllowlisted" && d.Detail.Contains(WalletB));
    }

    [Fact]
    public async Task Zero_balance_addresses_are_not_counted_as_holders()
    {
        GivenHolders((WalletA, 1_000, true), (WalletB, 0, false));
        GivenPlaced(1_000);
        GivenChainSupply(1_000);

        var result = await NewHandler().Handle(new ReconcileRegistryQuery(_property.Id), CancellationToken.None);

        result.Value.IsClean.Should().BeTrue("an address holding nothing is not an unlisted holder");
    }

    /// <summary>
    /// A node that cannot be reached is not evidence that everything agrees. Reporting "clean"
    /// because the chain could not be asked is precisely the failure this check exists to prevent.
    /// </summary>
    [Fact]
    public async Task An_unreachable_node_is_an_error_not_a_clean_report()
    {
        GivenHolders((WalletA, 1_000, true));
        GivenPlaced(1_000);
        _tokens.GetTotalSupplyAsync("bsc-testnet", "0xcontract", Arg.Any<CancellationToken>())
            .Returns<decimal>(_ => throw new HttpRequestException("node down"));

        var result = await NewHandler().Handle(new ReconcileRegistryQuery(_property.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.ExternalService);
    }

    [Fact]
    public async Task An_issue_with_no_contract_has_nothing_to_reconcile_against()
    {
        var draft = Property.Create("No contract yet", null, null, 1_000m, 10m, 100, "KGS");
        _properties.GetByIdAsync(draft.Id, Arg.Any<CancellationToken>()).Returns(draft);

        var result = await NewHandler().Handle(new ReconcileRegistryQuery(draft.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }
}
