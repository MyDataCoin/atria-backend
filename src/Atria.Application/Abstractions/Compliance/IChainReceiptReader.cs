namespace Atria.Application.Abstractions;

/// <summary>What the chain says about a submitted transaction.</summary>
/// <param name="Succeeded">
/// False for a mined transaction that reverted. It consumed gas and changed nothing — the opposite
/// of success, however finalized it is.
/// </param>
/// <param name="BlockNumber">Block it was mined in.</param>
/// <param name="Confirmations">Blocks on top of it, including its own.</param>
public sealed record ChainReceipt(bool Succeeded, long BlockNumber, long Confirmations);

/// <summary>
/// Reads transaction receipts from a network, so a submitted operation can be confirmed against what
/// actually happened rather than assumed.
/// </summary>
/// <remarks>
/// A transaction that has been broadcast is not a transaction that has settled: it can sit unmined,
/// it can revert, and on a reorg it can be mined and then un-mined. Anything the platform reports as
/// confirmed has to come from here.
/// </remarks>
public interface IChainReceiptReader
{
    /// <summary>The receipt, or null when the transaction is not mined yet (or unknown to the node).</summary>
    Task<ChainReceipt?> GetReceiptAsync(string chainTag, string transactionHash, CancellationToken ct);
}
