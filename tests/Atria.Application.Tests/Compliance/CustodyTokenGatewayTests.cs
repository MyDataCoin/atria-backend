using Atria.Application.Abstractions;
using Atria.Infrastructure.Compliance;
using Atria.Infrastructure.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Atria.Application.Tests.Compliance;

/// <summary>
/// The production minting path. Custody holds the key; the backend's job is to hand it a transaction
/// it can verify against the contract's ABI rather than a description of intent to re-derive.
/// </summary>
public sealed class CustodyTokenGatewayTests
{
    private readonly IBlockchainSigner _signer = Substitute.For<IBlockchainSigner>();

    private const string Contract = "0xdDedDC32271975c30856948E1e9dCaD31C548c90";
    private const string Wallet = "0xaaaa000000000000000000000000000000000000";

    private static readonly ChainNetworkResolver Networks = new(Options.Create(new BlockchainOptions
    {
        SignerUrl = "https://signer.test",
        Networks = new Dictionary<string, ChainNetworkOptions>
        {
            ["bsc-testnet"] = new()
            {
                ChainId = 97,
                RpcUrl = "https://rpc.test",
                IdentityRegistryAddress = "0x3838f73f9787f8b4f8a1e0173de7c7030a570806",
                AllowlistAddress = "0x5f1586bDaCD7bbD6da2E8EB377C51A268afd40D2"
            }
        }
    }));

    private CustodyTokenGateway NewGateway()
    {
        _signer.SignAndSubmitAsync(Arg.Any<SigningRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SigningResult("0xsigned", "0xsubmitted"));
        return new CustodyTokenGateway(_signer, Networks, NullLogger<CustodyTokenGateway>.Instance);
    }

    private SigningRequest CapturedRequest()
        => (SigningRequest)_signer.ReceivedCalls().Single().GetArguments()[0]!;

    [Fact]
    public async Task The_signer_is_handed_the_issues_own_contract_and_network()
    {
        await NewGateway().MintAsync("bsc-testnet", Contract, Wallet, 25, CancellationToken.None);

        var request = CapturedRequest();
        request.TokenContractAddress.Should().Be(Contract);
        request.ChainId.Should().Be("97");
        request.OperationType.Should().Be("TokenAllocation");
    }

    /// <summary>
    /// The payload carries encoded call data, not prose. The selector of mint(address,uint256) is
    /// 0x40c10f19, and the amount is the share count as written — decimals = 0.
    /// </summary>
    [Fact]
    public async Task The_payload_carries_encoded_mint_call_data()
    {
        await NewGateway().MintAsync("bsc-testnet", Contract, Wallet, 25, CancellationToken.None);

        var payload = CapturedRequest().UnsignedPayload;
        payload.Should().Contain("0x40c10f19");
        payload.Should().Contain(Wallet[2..].ToLowerInvariant());
        // 25 in the uint256 slot, right-aligned.
        payload.Should().Contain("19");
        payload.Should().Contain(Contract);
    }

    /// <summary>Custody returns a reference, not a receipt: submitted is not the same as mined.</summary>
    [Fact]
    public async Task A_custody_mint_is_reported_as_submitted_not_confirmed()
    {
        var result = await NewGateway().MintAsync("bsc-testnet", Contract, Wallet, 25, CancellationToken.None);

        result.TransactionRef.Should().Be("0xsubmitted");
        result.Confirmed.Should().BeFalse();
    }

    [Fact]
    public async Task An_unconfigured_network_is_refused_before_anything_is_signed()
    {
        var act = async () => await NewGateway()
            .MintAsync("ethereum-mainnet", Contract, Wallet, 25, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not configured*");
        await _signer.DidNotReceive().SignAndSubmitAsync(
            Arg.Any<SigningRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task A_non_positive_amount_is_refused(long amount)
    {
        var act = async () => await NewGateway()
            .MintAsync("bsc-testnet", Contract, Wallet, amount, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
