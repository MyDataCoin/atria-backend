using Atria.Application.Abstractions;
using Atria.Application.Properties.Commands;
using Atria.Domain.Audit;
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
    private readonly ITokenReader _tokens = Substitute.For<ITokenReader>();
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
        // Default: the node is unreachable, so the id cannot be established either way. Tests that
        // care about the check say what the contract answers.
        _tokens.GetPropertyIdAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        // Same default for the supply cap: unknown, so it neither blocks nor waves anything through.
        _tokens.GetMaxSupplyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((long?)null);
        // Scale defaults to the one the platform counts at, so only the tests that care say otherwise.
        _tokens.GetDecimalsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TokenAmount.Scale);
    }

    private void GivenContractDecimals(int? decimals)
        => _tokens.GetDecimalsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(decimals);

    private void GivenContractCap(long? cap)
        => _tokens.GetMaxSupplyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(cap);

    private Property GivenIssue(string? boundTo = null)
    {
        var property = Property.Create("Tower One", null, null, 1_000_000m, 100m, 1_000, "KGS");
        if (boundTo is not null)
            property.SetTokenContract(boundTo, "bsc-testnet", Issuer.ToLowerInvariant());

        _properties.GetByIdAsync(property.Id, Arg.Any<CancellationToken>()).Returns(property);
        return property;
    }

    private SetPropertyTokenContractCommandHandler NewHandler() =>
        new(_properties, _networks, _holders, _mintLists, _tokens, _audit, _uow);

    private void GivenContractDeclares(string propertyIdWord)
        => _tokens.GetPropertyIdAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(propertyIdWord);

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
                property.Id, Issuer.ToLowerInvariant(), 10, null, true,
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
                property.Id, Issuer.ToLowerInvariant(), 10, null, true,
                HolderSource.Chain, DateTime.UtcNow)
        });

        var result = await NewHandler().Handle(Bind(property.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// The contract naming the issue is what makes the register checkable rather than asserted, so a
    /// contract that names a different one is refused however plausible the address looks.
    /// </summary>
    [Fact]
    public async Task A_contract_belonging_to_another_issue_is_refused()
    {
        var property = GivenIssue();
        GivenContractDeclares(PropertyChainId.From(Guid.NewGuid()));

        var result = await NewHandler().Handle(Bind(property.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("property.tokenContractIdMismatch");
        property.TokenContractAddress.Should().BeNull();
    }

    /// <summary>The zero placeholder identifies nothing; the id is immutable, so it means a redeploy.</summary>
    [Fact]
    public async Task A_contract_deployed_without_its_property_id_is_refused()
    {
        var property = GivenIssue();
        GivenContractDeclares(PropertyChainId.Unset);

        var result = await NewHandler().Handle(Bind(property.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("property.tokenContractIdMismatch");
        result.Error.Message.Should().Contain(PropertyChainId.From(property.Id));
    }

    [Fact]
    public async Task A_contract_naming_this_issue_is_bound()
    {
        var property = GivenIssue();
        GivenContractDeclares(PropertyChainId.From(property.Id));

        var result = await NewHandler().Handle(Bind(property.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        property.TokenContractAddress.Should().Be(Contract.ToLowerInvariant());
    }

    /// <summary>
    /// An unreachable node must not read as a mismatch — that would refuse correct addresses whenever
    /// the RPC is down. It is journalled as unverified instead.
    /// </summary>
    [Fact]
    public async Task An_unreachable_contract_does_not_block_the_binding()
    {
        var property = GivenIssue();

        var result = await NewHandler().Handle(Bind(property.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _audit.Received(1).WriteAsync(
            Arg.Any<string>(), property.Id, Arg.Any<string>(), Arg.Any<string>(),
            AuditSeverity.Warning, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_property_is_not_found()
    {
        var result = await NewHandler().Handle(Bind(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("property.notFound");
    }

    /// <summary>
    /// TOKEN_MAX_SUPPLY is immutable after deployment, so an issue larger than the cap is not a
    /// problem for later — it is an issue whose last shares can never be minted.
    /// </summary>
    [Fact]
    public async Task An_issue_larger_than_the_contracts_cap_is_refused()
    {
        var property = GivenIssue();          // 1 000 долей
        GivenContractCap(900);

        var result = await NewHandler().Handle(Bind(property.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("property.issueExceedsMaxSupply");
        result.Error.Message.Should().Contain("1000").And.Contain("900");
        property.TokenContractAddress.Should().BeNull("привязка не должна состояться");
    }

    [Fact]
    public async Task A_cap_that_exactly_covers_the_issue_binds()
    {
        var property = GivenIssue();
        GivenContractCap(1_000);

        var result = await NewHandler().Handle(Bind(property.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        property.TokenContractAddress.Should().Be(Contract.ToLowerInvariant());
    }

    /// <summary>
    /// An unreachable node leaves the cap unknown, and unknown is not "unlimited": the binding goes
    /// through — refusing correct addresses because a node blinked would be worse — but it is
    /// journalled as unverified so a suspect register can be traced back to this moment.
    /// </summary>
    [Fact]
    public async Task An_unreadable_cap_does_not_block_the_binding_but_is_journalled_as_a_warning()
    {
        var property = GivenIssue();
        GivenContractDeclares(PropertyChainId.From(property.Id));
        GivenContractCap(null);

        var result = await NewHandler().Handle(Bind(property.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _audit.Received().WriteAsync(
            Arg.Any<string>(), property.Id, Arg.Any<string>(),
            Arg.Is<string>(m => m.Contains("maxSupply прочитать не удалось")),
            AuditSeverity.Warning, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A token on the wrong scale keeps working and means something else by every number: at
    /// decimals 2 a mint of 100 is one share, not a hundred. Nothing downstream can tell the
    /// difference, because both sides are plain integers — so it is caught at the binding.
    /// </summary>
    [Fact]
    public async Task A_contract_on_a_different_scale_is_refused()
    {
        var property = GivenIssue();
        GivenContractDecimals(2);

        var result = await NewHandler().Handle(Bind(property.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("property.tokenDecimalsMismatch");
        property.TokenContractAddress.Should().BeNull();
    }

    [Fact]
    public async Task An_unreadable_scale_does_not_block_the_binding_but_is_journalled()
    {
        var property = GivenIssue();
        GivenContractDeclares(PropertyChainId.From(property.Id));
        GivenContractCap(1_000);
        GivenContractDecimals(null);

        var result = await NewHandler().Handle(Bind(property.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _audit.Received().WriteAsync(
            Arg.Any<string>(), property.Id, Arg.Any<string>(),
            Arg.Is<string>(m => m.Contains("decimals() прочитать не удалось")),
            AuditSeverity.Warning, Arg.Any<CancellationToken>());
    }
}
