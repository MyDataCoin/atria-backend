using System.Collections.Concurrent;
using System.Numerics;
using Atria.Application.Abstractions;
using Atria.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;

namespace Atria.Infrastructure.Compliance;

/// <summary>
/// Mints shares by calling the issue's own token contract directly, signed by the minter key held in
/// configuration.
/// </summary>
/// <remarks>
/// <para>
/// Tessera has no minting adapter — its abstractions cover anchoring and the allowlist only — so
/// this is written to the same shape as its EVM gateway: a cached Web3 client per network, legacy
/// gas pricing for BNB Chain, and a wait for the receipt so the caller learns whether the write
/// actually landed.
/// </para>
/// <para>
/// This path is for networks where the platform holds the minter key — a test network. In
/// production the minter is external custody and <see cref="CustodyTokenGateway"/> is used instead;
/// which one is live is a configuration choice, and the default is custody. A key that can create
/// shares out of nothing does not belong in an application process by default.
/// </para>
/// </remarks>
public sealed class EvmTokenGateway : ITokenGateway
{
    private readonly IChainNetworkResolver _networks;
    private readonly TokenSigningOptions _options;
    private readonly ILogger<EvmTokenGateway> _logger;
    private readonly ConcurrentDictionary<string, Web3> _clients = new();

    public EvmTokenGateway(
        IChainNetworkResolver networks,
        IOptions<TokenSigningOptions> options,
        ILogger<EvmTokenGateway> logger)
    {
        _networks = networks;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TokenWriteResult> MintAsync(
        string chainTag, string tokenContractAddress, string toAddress, long amount, CancellationToken ct)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Mint amount must be positive.");

        var web3 = ClientFor(chainTag);
        var handler = web3.Eth.GetContractTransactionHandler<MintFunction>();

        // decimals = 0 on this token: one unit is one whole share, so the amount is passed through
        // untouched. Scaling it by 10^18 the way an ordinary ERC-20 would expect would mint a
        // million million shares.
        var mint = new MintFunction { To = toAddress, Amount = new BigInteger(amount) };

        var receipt = await handler.SendRequestAndWaitForReceiptAsync(tokenContractAddress, mint, null);

        // A receipt with status 0 is a reverted transaction — most often the address never made it
        // onto the allowlist. It costs gas and changes nothing, and reporting it as a successful
        // mint would leave the registry counting shares that do not exist.
        var succeeded = receipt.Status?.Value == 1;

        _logger.Log(
            succeeded ? LogLevel.Information : LogLevel.Error,
            "Mint of {Amount} share(s) to {To} on {Contract} ({Chain}): tx {TxHash}, status {Status}.",
            amount, toAddress, tokenContractAddress, chainTag, receipt.TransactionHash,
            succeeded ? "confirmed" : "REVERTED");

        if (!succeeded)
            throw new InvalidOperationException(
                $"Mint reverted on chain (tx {receipt.TransactionHash}). The recipient is most likely "
                + "not on the allowlist, or the issue's cap would be exceeded.");

        return new TokenWriteResult(receipt.TransactionHash, Confirmed: true);
    }

    private Web3 ClientFor(string chainTag)
    {
        var network = _networks.Resolve(chainTag)
            ?? throw new InvalidOperationException(
                $"Chain '{chainTag}' is not configured under Blockchain:Networks.");

        return _clients.GetOrAdd(network.Tag, _ =>
        {
            if (_options.IsMisconfigured)
                throw new InvalidOperationException(
                    "Blockchain:TokenSigning is set to OperationalKey but no minter key was supplied.");

            var account = new Account(_options.MinterPrivateKey, network.ChainId);
            var web3 = new Web3(account, network.RpcUrl);

            // BNB Chain rejects EIP-1559 transactions with a zero priority fee, which is what the
            // automatic fee path produces there.
            web3.TransactionManager.UseLegacyAsDefault = true;
            return web3;
        });
    }

    /// <summary>ABI binding for <c>mint(address,uint256)</c> on the property token.</summary>
    [Function("mint")]
    private sealed class MintFunction : FunctionMessage
    {
        [Parameter("address", "to", 1)]
        public string To { get; set; } = null!;

        [Parameter("uint256", "amount", 2)]
        public BigInteger Amount { get; set; }
    }
}
