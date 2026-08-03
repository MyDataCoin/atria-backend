using System.Collections.Concurrent;
using System.Numerics;
using Atria.Application.Abstractions;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;

namespace Atria.Infrastructure.Compliance;

/// <summary>
/// Reads an EVM token contract over JSON-RPC. No key, no signing — a wrong answer here cannot be
/// caused by anything this class does.
/// </summary>
public sealed class EvmTokenReader : ITokenReader
{
    private readonly IChainNetworkResolver _networks;
    private readonly ConcurrentDictionary<string, Web3> _clients = new();

    public EvmTokenReader(IChainNetworkResolver networks) => _networks = networks;

    public async Task<long> GetBalanceAsync(
        string chainTag, string tokenContractAddress, string address, CancellationToken ct)
    {
        var handler = ClientFor(chainTag).Eth.GetContractQueryHandler<BalanceOfFunction>();
        var balance = await handler.QueryAsync<BigInteger>(
            tokenContractAddress, new BalanceOfFunction { Account = address });

        return ToShares(balance);
    }

    public async Task<long> GetTotalSupplyAsync(
        string chainTag, string tokenContractAddress, CancellationToken ct)
    {
        var handler = ClientFor(chainTag).Eth.GetContractQueryHandler<TotalSupplyFunction>();
        var supply = await handler.QueryAsync<BigInteger>(tokenContractAddress, new TotalSupplyFunction());

        return ToShares(supply);
    }

    public async Task<long> GetHeadBlockAsync(string chainTag, CancellationToken ct)
    {
        var head = await ClientFor(chainTag).Eth.Blocks.GetBlockNumber.SendRequestAsync();
        return (long)head.Value;
    }

    public async Task<IReadOnlyList<TokenTransfer>> GetTransfersAsync(
        string chainTag, string tokenContractAddress, long fromBlock, long toBlock, CancellationToken ct)
    {
        if (toBlock < fromBlock)
            return Array.Empty<TokenTransfer>();

        var web3 = ClientFor(chainTag);
        var transferEvent = web3.Eth.GetEvent<TransferEventDto>(tokenContractAddress);

        var filter = transferEvent.CreateFilterInput(
            new BlockParameter(new HexBigInteger(fromBlock)),
            new BlockParameter(new HexBigInteger(toBlock)));

        var logs = await transferEvent.GetAllChangesAsync(filter);

        return logs
            .Select(log => new TokenTransfer(
                log.Event.From,
                log.Event.To,
                ToShares(log.Event.Value),
                (long)log.Log.BlockNumber.Value,
                log.Log.TransactionHash))
            .OrderBy(t => t.BlockNumber)
            .ToList();
    }

    /// <summary>
    /// The token has <c>decimals = 0</c>, so a raw amount is already a whole share count. A value too
    /// large for a share count means the contract is not the one we think it is, and quietly
    /// truncating it would put a fabricated number into the registry.
    /// </summary>
    private static long ToShares(BigInteger raw)
        => raw > long.MaxValue || raw < 0
            ? throw new InvalidOperationException($"Share amount {raw} is out of range for this issue.")
            : (long)raw;

    private Web3 ClientFor(string chainTag)
    {
        var network = _networks.Resolve(chainTag)
            ?? throw new InvalidOperationException(
                $"Chain '{chainTag}' is not configured under Blockchain:Networks.");

        return _clients.GetOrAdd(network.Tag, _ => new Web3(network.RpcUrl));
    }

    [Function("balanceOf", "uint256")]
    private sealed class BalanceOfFunction : FunctionMessage
    {
        [Parameter("address", "account", 1)]
        public string Account { get; set; } = null!;
    }

    [Function("totalSupply", "uint256")]
    private sealed class TotalSupplyFunction : FunctionMessage;

    [Event("Transfer")]
    private sealed class TransferEventDto : IEventDTO
    {
        [Parameter("address", "from", 1, true)]
        public string From { get; set; } = null!;

        [Parameter("address", "to", 2, true)]
        public string To { get; set; } = null!;

        [Parameter("uint256", "value", 3, false)]
        public BigInteger Value { get; set; }
    }
}
