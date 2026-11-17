using System.Collections.Concurrent;
using System.Numerics;
using Atria.Application.Abstractions;
using Atria.Application.Payments;
using Atria.Domain.Payments;
using Microsoft.Extensions.Options;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;

namespace Atria.Infrastructure.Payments;

/// <summary>
/// Incoming BEP-20 transfers (USDT, KGST) on BNB Smart Chain MAINNET, read with eth_getLogs filtered on
/// the indexed <c>to</c> topic. Only blocks at least <see cref="CryptoPaymentOptions.BscConfirmations"/>
/// deep are read, so a transfer that a reorg could still undo never settles an invoice.
/// </summary>
public sealed class BscTransferSource : ICryptoTransferSource
{
    private readonly CryptoPaymentOptions _options;
    private readonly Web3 _web3;
    private readonly ConcurrentDictionary<string, int> _decimals = new(StringComparer.OrdinalIgnoreCase);

    public BscTransferSource(IOptions<CryptoPaymentOptions> options)
    {
        _options = options.Value;
        _web3 = new Web3(_options.BscRpcUrl);
    }

    public bool Supports(CryptoPaymentMethod method)
        => method is CryptoPaymentMethod.UsdtBep20 or CryptoPaymentMethod.KgstBep20;

    public async Task<long> GetStartPositionAsync(CryptoPaymentMethod method, CancellationToken ct)
        => await SafeHeadAsync();

    public async Task<TransferPage> ReadAsync(
        CryptoPaymentMethod method, string tokenContract, string toAddress, long fromPosition, CancellationToken ct)
    {
        var safeHead = await SafeHeadAsync();
        if (fromPosition > safeHead)
            return new TransferPage(Array.Empty<IncomingTransfer>(), fromPosition);

        var toBlock = Math.Min(fromPosition + _options.BscMaxBlockRange - 1, safeHead);

        var transferEvent = _web3.Eth.GetEvent<TransferEventDto>(tokenContract);
        var filter = transferEvent.CreateFilterInput(
            null,
            new object[] { toAddress },
            new BlockParameter(new HexBigInteger(fromPosition)),
            new BlockParameter(new HexBigInteger(toBlock)));
        var logs = await transferEvent.GetAllChangesAsync(filter);

        if (logs.Count == 0)
            return new TransferPage(Array.Empty<IncomingTransfer>(), toBlock + 1);

        var decimals = await DecimalsAsync(tokenContract);
        var blockTimes = new Dictionary<BigInteger, DateTime>();
        var transfers = new List<IncomingTransfer>(logs.Count);

        foreach (var log in logs.OrderBy(l => l.Log.BlockNumber.Value).ThenBy(l => l.Log.LogIndex.Value))
        {
            var block = log.Log.BlockNumber.Value;
            if (!blockTimes.TryGetValue(block, out var at))
            {
                var header = await _web3.Eth.Blocks.GetBlockWithTransactionsHashesByNumber
                    .SendRequestAsync(new HexBigInteger(block));
                at = DateTimeOffset.FromUnixTimeSeconds((long)header.Timestamp.Value).UtcDateTime;
                blockTimes[block] = at;
            }

            transfers.Add(new IncomingTransfer(
                $"{log.Log.TransactionHash}:{log.Log.LogIndex.Value}",
                log.Log.TransactionHash,
                log.Event.From,
                log.Event.To,
                ToUnits(log.Event.Value, decimals),
                at));
        }

        return new TransferPage(transfers, toBlock + 1);
    }

    private async Task<long> SafeHeadAsync()
    {
        var head = await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
        return (long)head.Value - _options.BscConfirmations;
    }

    private async Task<int> DecimalsAsync(string tokenContract)
    {
        if (_decimals.TryGetValue(tokenContract, out var cached))
            return cached;

        var handler = _web3.Eth.GetContractQueryHandler<DecimalsFunction>();
        var value = await handler.QueryAsync<byte>(tokenContract, new DecimalsFunction());
        _decimals[tokenContract] = value;
        return value;
    }

    /// <summary>Exact conversion of a raw integer amount to token units — no floating point.</summary>
    internal static decimal ToUnits(BigInteger raw, int decimals)
    {
        var scale = BigInteger.Pow(10, decimals);
        var whole = BigInteger.DivRem(raw, scale, out var remainder);
        return (decimal)whole + (decimal)remainder / (decimal)scale;
    }

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

    [Function("decimals", "uint8")]
    private sealed class DecimalsFunction : FunctionMessage { }
}
