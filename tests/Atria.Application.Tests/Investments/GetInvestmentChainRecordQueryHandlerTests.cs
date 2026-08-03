using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Investments.Queries;
using Atria.Domain.Factories;
using Atria.Domain.Investments;
using Atria.Domain.Users;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Investments;

/// <summary>
/// §21: the holder can check their shares against the public record. What matters is that the
/// coordinates handed out are the real ones, that nothing is invented when a step has not happened,
/// and that they only reach the person whose holding they describe.
/// </summary>
public sealed class GetInvestmentChainRecordQueryHandlerTests
{
    private readonly IInvestmentRepository _investments = Substitute.For<IInvestmentRepository>();
    private readonly IPropertyRepository _properties = Substitute.For<IPropertyRepository>();
    private readonly IChainNetworkResolver _networks = Substitute.For<IChainNetworkResolver>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();

    private const string Wallet = "0x1111111111111111111111111111111111111111";
    private const string Contract = "0x2222222222222222222222222222222222222222";
    private const string TxHash = "0xabc0000000000000000000000000000000000000000000000000000000000001";

    private static readonly DateTime Now = new(2026, 8, 4, 10, 0, 0, DateTimeKind.Utc);

    public GetInvestmentChainRecordQueryHandlerTests()
    {
        _networks.ConfirmationsRequired.Returns(15);
        _networks.Resolve("bsc-testnet").Returns(new ChainNetwork(
            "bsc-testnet", 97, "https://rpc.invalid", "0xreg", "0xallow",
            "https://testnet.bscscan.com/"));
    }

    private Investment GivenMintedInvestment(Guid investorId, bool minted = true)
    {
        var property = Property.Create("Tower One", null, null, 1_000_000m, 100m, 1_000, "KGS");
        property.Publish();
        property.SetTokenContract(Contract, "bsc-testnet", "0xissuer");

        var investment = InvestmentFactory.CreateForInvestor(
            investorId, property.Id, 10, 1_000m, "KGS", 100m, Now.AddDays(3));
        investment.Approve(Now);

        if (minted)
        {
            investment.SetTokenTarget(Wallet, Contract);
            investment.SetOnChainResult(TxHash, OnChainStatus.Confirmed);
        }

        _investments.GetByIdAsync(investment.Id, Arg.Any<CancellationToken>()).Returns(investment);
        _properties.GetByIdAsync(property.Id, Arg.Any<CancellationToken>()).Returns(property);
        return investment;
    }

    private GetInvestmentChainRecordQueryHandler NewHandler()
        => new(_investments, _properties, _networks, _currentUser);

    [Fact]
    public async Task The_holder_sees_the_coordinates_of_their_own_shares()
    {
        var investorId = Guid.NewGuid();
        _currentUser.UserId.Returns(investorId);
        var investment = GivenMintedInvestment(investorId);

        var result = await NewHandler().Handle(
            new GetInvestmentChainRecordQuery(investment.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var record = result.Value;
        record.WalletAddress.Should().Be(Wallet);
        record.TokenContractAddress.Should().Be(Contract);
        record.TransactionHash.Should().Be(TxHash);
        record.Status.Should().Be(OnChainStatus.Confirmed);
        record.ChainTag.Should().Be("bsc-testnet");
        record.ChainId.Should().Be(97);
        record.ConfirmationsRequired.Should().Be(15);
    }

    [Fact]
    public async Task Explorer_links_point_at_the_network_the_issue_lives_on()
    {
        var investorId = Guid.NewGuid();
        _currentUser.UserId.Returns(investorId);
        var investment = GivenMintedInvestment(investorId);

        var record = (await NewHandler().Handle(
            new GetInvestmentChainRecordQuery(investment.Id), CancellationToken.None)).Value;

        record.TransactionUrl.Should().Be($"https://testnet.bscscan.com/tx/{TxHash}");
        record.WalletUrl.Should().Be($"https://testnet.bscscan.com/address/{Wallet}");
        record.ContractUrl.Should().Be($"https://testnet.bscscan.com/address/{Contract}");
    }

    /// <summary>
    /// A hash for an allocation that never happened would be worse than an empty field: the holder
    /// would follow it to an explorer page that says nothing exists.
    /// </summary>
    [Fact]
    public async Task An_unminted_investment_reports_no_transaction_rather_than_a_made_up_one()
    {
        var investorId = Guid.NewGuid();
        _currentUser.UserId.Returns(investorId);
        var investment = GivenMintedInvestment(investorId, minted: false);

        var record = (await NewHandler().Handle(
            new GetInvestmentChainRecordQuery(investment.Id), CancellationToken.None)).Value;

        record.Status.Should().Be(OnChainStatus.None);
        record.TransactionHash.Should().BeNull();
        record.TransactionUrl.Should().BeNull();
        record.WalletUrl.Should().BeNull("no address has been recorded for this allocation");
        record.ChainTag.Should().Be("bsc-testnet", "the issue still names its network");
    }

    [Fact]
    public async Task A_network_with_no_explorer_still_yields_the_addresses()
    {
        var investorId = Guid.NewGuid();
        _currentUser.UserId.Returns(investorId);
        _networks.Resolve("bsc-testnet").Returns(new ChainNetwork(
            "bsc-testnet", 97, "https://rpc.invalid", "0xreg", "0xallow", ExplorerBaseUrl: null));
        var investment = GivenMintedInvestment(investorId);

        var record = (await NewHandler().Handle(
            new GetInvestmentChainRecordQuery(investment.Id), CancellationToken.None)).Value;

        record.TransactionHash.Should().Be(TxHash);
        record.TransactionUrl.Should().BeNull();
        record.ContractUrl.Should().BeNull();
    }

    /// <summary>
    /// An address and a transaction hash together identify a person's holding, so a stranger is told
    /// the investment does not exist rather than that it is not theirs.
    /// </summary>
    [Fact]
    public async Task Another_investor_is_not_told_the_investment_exists()
    {
        var investment = GivenMintedInvestment(Guid.NewGuid());
        _currentUser.UserId.Returns(Guid.NewGuid());
        _currentUser.IsInRole(Role.Admin).Returns(false);

        var result = await NewHandler().Handle(
            new GetInvestmentChainRecordQuery(investment.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task An_admin_may_read_any_record()
    {
        var investment = GivenMintedInvestment(Guid.NewGuid());
        _currentUser.UserId.Returns(Guid.NewGuid());
        _currentUser.IsInRole(Role.Admin).Returns(true);

        var result = await NewHandler().Handle(
            new GetInvestmentChainRecordQuery(investment.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TransactionHash.Should().Be(TxHash);
    }

    [Fact]
    public async Task An_anonymous_caller_gets_nothing()
    {
        var investment = GivenMintedInvestment(Guid.NewGuid());
        _currentUser.UserId.Returns((Guid?)null);

        var result = await NewHandler().Handle(
            new GetInvestmentChainRecordQuery(investment.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }
}
