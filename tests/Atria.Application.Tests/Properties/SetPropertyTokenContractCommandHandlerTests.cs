using Atria.Application.Abstractions;
using Atria.Application.Properties.Commands;
using Atria.Domain.Holders;
using Atria.Domain.Investments;
using Atria.Domain.Whitelist;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Properties;

/// <summary>
/// Binding a token contract to an issue is what makes every chain-facing feature of that issue real,
/// and moving the binding afterwards is what would quietly detach the register from the shares it
/// describes. Both halves are covered here.
/// </summary>
public sealed class SetPropertyTokenContractCommandHandlerTests
{
    private const string Contract = "0xdDedDC32e07A2Fd0e1cC0F0b0a9F4E6F8dC3f9A1";
    private const string Issuer = "0x50FBA2003D9f16EefE29828210A1120DBD5368fE";

    private readonly IPropertyRepository _properties = Substitute.For<IPropertyRepository>();
    private readonly IChainNetworkResolver _networks = Substitute.For<IChainNetworkResolver>();
    private readonly IHolderPositionRepository _holders = Substitute.For<IHolderPositionRepository>();
    private readonly IMintListRepository _mintLists = Substitute.For<IMintListRepository>();
    private readonly IAuditWriter _audit = Substitute.For<IAuditWriter>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    public SetPropertyTokenContractCommandHandlerTests()
    {
        var network = new ChainNetwork("bsc-testnet", 97, "https://rpc", "0xregistry", "0xallowlist");
        _networks.Resolve("bsc-testnet").Returns(network);
        _networks.All.Returns(new[] { network });
        _holders.GetByPropertyAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<HolderPosition>());
        _mintLists.ListAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<(MintList, string?)>());
    }

    private Property GivenIssue(string? boundTo = null)
    {
        var property = Property.Create("Tower One", null, null, 1_000_000m, 100m, 1_000, "KGS");
        if (boundTo is not null)
            property.SetTokenContract(boundTo, "bsc-testnet", Issuer.ToLowerInvariant());

        _properties.GetByIdAsync(property.Id, Arg.Any<CancellationToken>()).Returns(property);
        return property;
    }

    private SetPropertyTokenContractCommandHandler NewHandler() =>
        new(_properties, _networks, _holders, _mintLists, _audit, _uow);

    private static SetPropertyTokenContractCommand Bind(
        Guid id, string contract = Contract, string chain = "bsc-testnet") =>
        new(id, contract, chain, Issuer);

    [Fact]
    public async Task Binding_records_the_contract_lowercased()
    {
        var property = GivenIssue();

        var result = await NewHandler().Handle(Bind(property.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        property.TokenContractAddress.Should().Be(Contract.ToLowerInvariant());
        property.TokenChain.Should().Be("bsc-testnet");
        property.IssuerWalletAddress.Should().Be(Issuer.ToLowerInvariant());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_malformed_contract_address_is_refused()
    {
        var property = GivenIssue();

        var result = await NewHandler().Handle(Bind(property.Id, "0xcontract"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("property.invalidTokenContract");
        property.TokenContractAddress.Should().BeNull();
    }

    /// <summary>A tag no network resolves to would only surface as a mint that never leaves the queue.</summary>
    [Fact]
    public async Task An_unconfigured_network_is_refused()
    {
        var property = GivenIssue();

        var result = await NewHandler().Handle(
            Bind(property.Id, chain: "bsc-mainnet"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("property.unknownChain");
        result.Error.Message.Should().Contain("bsc-testnet");
    }

    /// <summary>Nothing issued yet, so a contract deployed by mistake can still be replaced.</summary>
    [Fact]
    public async Task Rebinding_is_allowed_while_nothing_has_been_issued()
    {
        var property = GivenIssue(boundTo: "0x1111111111111111111111111111111111111111");

        var result = await NewHandler().Handle(Bind(property.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        property.TokenContractAddress.Should().Be(Contract.ToLowerInvariant());
    }

    [Fact]
    public async Task Rebinding_is_refused_once_a_holder_position_exists()
    {
        var property = GivenIssue(boundTo: "0x1111111111111111111111111111111111111111");
        _holders.GetByPropertyAsync(property.Id, Arg.Any<CancellationToken>()).Returns(new[]
        {
            HolderPosition.Create(
                property.Id, Issuer.ToLowerInvariant(), 10m, null, true,
                HolderSource.Chain, DateTime.UtcNow)
        });

        var result = await NewHandler().Handle(Bind(property.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("property.tokenContractAlreadyBound");
        property.TokenContractAddress.Should().Be("0x1111111111111111111111111111111111111111");
    }

    /// <summary>
    /// The issuer wallet is still correctable after issuance: it points at a position, not at the
    /// shares themselves, so re-sending the same contract must not trip the guard.
    /// </summary>
    [Fact]
    public async Task Re_sending_the_same_binding_is_not_treated_as_a_move()
    {
        var property = GivenIssue(boundTo: Contract.ToLowerInvariant());
        _holders.GetByPropertyAsync(property.Id, Arg.Any<CancellationToken>()).Returns(new[]
        {
            HolderPosition.Create(
                property.Id, Issuer.ToLowerInvariant(), 10m, null, true,
                HolderSource.Chain, DateTime.UtcNow)
        });

        var result = await NewHandler().Handle(Bind(property.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task An_unknown_property_is_not_found()
    {
        var result = await NewHandler().Handle(Bind(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("property.notFound");
    }
}
