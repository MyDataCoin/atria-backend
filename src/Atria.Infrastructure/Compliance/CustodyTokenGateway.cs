using System.Text.Json;
using Atria.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Nethereum.ABI.FunctionEncoding;
using Nethereum.ABI.Model;
using Nethereum.Hex.HexTypes;
using Atria.Domain.Investments;

namespace Atria.Infrastructure.Compliance;

/// <summary>
/// Mints through the external custody service: the backend builds the exact transaction and custody
/// holds the key that signs it. The production posture — nothing here can issue shares on its own.
/// </summary>
/// <remarks>
/// The call data is ABI-encoded on this side rather than described in prose for the signer to
/// interpret. Custody then signs a byte string it can check against the contract's ABI, instead of
/// re-deriving our intent from a JSON blob and possibly deriving it differently.
/// </remarks>
public sealed class CustodyTokenGateway : ITokenGateway
{
    private readonly IBlockchainSigner _signer;
    private readonly IChainNetworkResolver _networks;
    private readonly ILogger<CustodyTokenGateway> _logger;

    public CustodyTokenGateway(
        IBlockchainSigner signer, IChainNetworkResolver networks, ILogger<CustodyTokenGateway> logger)
    {
        _signer = signer;
        _networks = networks;
        _logger = logger;
    }

    public async Task<TokenWriteResult> MintAsync(
        string chainTag, string tokenContractAddress, string toAddress, long amount, CancellationToken ct)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Mint amount must be positive.");

        var network = _networks.Resolve(chainTag)
            ?? throw new InvalidOperationException(
                $"Chain '{chainTag}' is not configured under Blockchain:Networks.");

        // mint(address,uint256). decimals() == TokenAmount.Scale is zero, so the share count IS the
        // contract's unit; it still goes through ToMinor so the conversion has one home.
        var minorUnits = TokenAmount.ToMinor(amount);
        var callData = new FunctionCallEncoder().EncodeRequest(
            sha3Signature: "40c10f19",
            parameters:
            [
                new Parameter("address", "to", 1),
                new Parameter("uint256", "amount", 2)
            ],
            values: [toAddress, minorUnits]);

        var payload = JsonSerializer.Serialize(new
        {
            to = tokenContractAddress,
            data = callData,
            chainId = network.ChainId,
            value = "0x0"
        });

        var result = await _signer.SignAndSubmitAsync(
            new SigningRequest("TokenAllocation", payload, network.ChainId.ToString(), tokenContractAddress),
            ct);

        var txRef = result.SubmissionReference ?? result.SignedPayload;

        _logger.LogInformation(
            "Submitted mint of {Amount} share(s) to {To} on {Contract} ({Chain}) to custody; ref {TxRef}.",
            amount, toAddress, tokenContractAddress, chainTag, txRef);

        // Submitted, not observed: custody returns a reference, and whether the transaction was
        // mined is a separate question the finality check answers.
        return new TokenWriteResult(txRef, Confirmed: false);
    }

    public async Task<TokenWriteResult> ReportCollateralAsync(
        string chainTag, string tokenContractAddress, byte[] dataHash, decimal valuation,
        DateTime valuedAtUtc, string uri, CancellationToken ct)
    {
        if (dataHash.Length != 32)
            throw new ArgumentException("The collateral data hash must be 32 bytes.", nameof(dataHash));

        var network = _networks.Resolve(chainTag)
            ?? throw new InvalidOperationException(
                $"Chain '{chainTag}' is not configured under Blockchain:Networks.");

        // reportCollateral(bytes32,uint256,uint64,string). Custody picks the oracle key by operation
        // type — the same reason the operation name travels with the request.
        var callData = new FunctionCallEncoder().EncodeRequest(
            sha3Signature: "d2b3d0db",
            parameters:
            [
                new Parameter("bytes32", "dataHash", 1),
                new Parameter("uint256", "valuation", 2),
                new Parameter("uint64", "valuedAt", 3),
                new Parameter("string", "uri", 4)
            ],
            values:
            [
                dataHash,
                decimal.Truncate(valuation * 100m),
                (ulong)new DateTimeOffset(valuedAtUtc, TimeSpan.Zero).ToUnixTimeSeconds(),
                uri ?? string.Empty
            ]);

        var payload = JsonSerializer.Serialize(new
        {
            to = tokenContractAddress,
            data = callData,
            chainId = network.ChainId,
            value = "0x0"
        });

        var result = await _signer.SignAndSubmitAsync(
            new SigningRequest("CollateralReport", payload, network.ChainId.ToString(), tokenContractAddress),
            ct);

        var txRef = result.SubmissionReference ?? result.SignedPayload;

        _logger.LogInformation(
            "Submitted a collateral report for {Contract} ({Chain}) to custody; ref {TxRef}.",
            tokenContractAddress, chainTag, txRef);

        return new TokenWriteResult(txRef, Confirmed: false);
    }
}
