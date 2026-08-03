using System.Collections.Concurrent;
using Atria.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Nethereum.Web3;

namespace Atria.Infrastructure.Compliance;

/// <summary>
/// Reads receipts from an EVM network over JSON-RPC. Read-only: it holds no key and signs nothing,
/// so it is created from the network's RPC endpoint alone.
/// </summary>
public sealed class EvmReceiptReader : IChainReceiptReader
{
    private readonly IChainNetworkResolver _networks;
    private readonly ILogger<EvmReceiptReader> _logger;
    private readonly ConcurrentDictionary<string, Web3> _clients = new();

    public EvmReceiptReader(IChainNetworkResolver networks, ILogger<EvmReceiptReader> logger)
    {
        _networks = networks;
        _logger = logger;
    }

    public async Task<ChainReceipt?> GetReceiptAsync(
        string chainTag, string transactionHash, CancellationToken ct)
    {
        var web3 = ClientFor(chainTag);

        var receipt = await web3.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(transactionHash);
        if (receipt is null)
        {
            // Not mined yet, or this node has not seen it. Either way there is nothing to confirm;
            // the caller leaves the operation submitted and asks again next pass.
            return null;
        }

        var head = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
        var blockNumber = (long)receipt.BlockNumber.Value;

        // Its own block counts as the first confirmation.
        var confirmations = (long)head.Value - blockNumber + 1;
        var succeeded = receipt.Status?.Value == 1;

        if (!succeeded)
        {
            _logger.LogError(
                "Transaction {TxHash} on {Chain} reverted in block {Block}.",
                transactionHash, chainTag, blockNumber);
        }

        return new ChainReceipt(succeeded, blockNumber, Math.Max(confirmations, 0));
    }

    private Web3 ClientFor(string chainTag)
    {
        var network = _networks.Resolve(chainTag)
            ?? throw new InvalidOperationException(
                $"Chain '{chainTag}' is not configured under Blockchain:Networks.");

        return _clients.GetOrAdd(network.Tag, _ => new Web3(network.RpcUrl));
    }
}
