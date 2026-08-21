namespace Atria.Application.Abstractions;

/// <summary>One share movement observed on chain.</summary>
/// <param name="From">Sender; the zero address for a mint.</param>
/// <param name="To">Recipient; the zero address for a burn.</param>
/// <param name="Amount">Whole shares moved.</param>
/// <param name="BlockNumber">Block the transfer was mined in.</param>
/// <param name="TransactionHash">Transaction that produced it.</param>
public sealed record TokenTransfer(
    string From, string To, long Amount, long BlockNumber, string TransactionHash);

/// <summary>
/// Reads an issue's token contract. Read-only by design: it holds no key, so nothing here can change
/// what it reports.
/// </summary>
/// <remarks>
/// This is what makes the holder registry checkable rather than merely believed. Until the platform
/// can ask the chain what it actually holds, the registry is a record of what the platform intended
/// to happen — and those two diverge quietly, through a reverted mint or a transfer between
/// investors that nobody told the backend about.
/// </remarks>
public interface ITokenReader
{
    /// <summary>Shares held by an address right now.</summary>
    Task<long> GetBalanceAsync(string chainTag, string tokenContractAddress, string address, CancellationToken ct);

    /// <summary>Total shares in existence on the contract.</summary>
    Task<long> GetTotalSupplyAsync(string chainTag, string tokenContractAddress, CancellationToken ct);

    /// <summary>
    /// The issue id the contract was deployed with, as a <c>bytes32</c> hex word, or null when the
    /// contract does not answer at all — it has no such function, or the node cannot be reached.
    /// </summary>
    /// <remarks>
    /// Null means "not established", never "does not match": a caller that treats an unreachable node
    /// as a mismatch would refuse correct addresses, and one that treats it as a match would defeat
    /// the check it is making.
    /// </remarks>
    Task<string?> GetPropertyIdAsync(string chainTag, string tokenContractAddress, CancellationToken ct);

    /// <summary>
    /// The ceiling the contract will ever mint to — its <c>maxSupply()</c>, set from the
    /// <c>TOKEN_MAX_SUPPLY</c> deployment variable and only ever decreasable. Null when the contract
    /// does not answer: no such function, or the node is unreachable. As with
    /// <see cref="GetPropertyIdAsync"/>, null means "not established", never "unlimited".
    /// </summary>
    Task<long?> GetMaxSupplyAsync(string chainTag, string tokenContractAddress, CancellationToken ct);

    /// <summary>
    /// The contract's <c>decimals()</c>. Null when it cannot be established (no such function, or an
    /// unreachable node) — never a guess, because a wrong scale here is exactly the mistake that
    /// makes the register and the chain describe different holdings.
    /// </summary>
    Task<int?> GetDecimalsAsync(string chainTag, string tokenContractAddress, CancellationToken ct);

    /// <summary>The block the chain is currently at.</summary>
    Task<long> GetHeadBlockAsync(string chainTag, CancellationToken ct);

    /// <summary>
    /// Transfer events in a block range, inclusive. Mints appear as transfers from the zero address
    /// and burns as transfers to it, which is how the whole history of an issue can be replayed.
    /// </summary>
    Task<IReadOnlyList<TokenTransfer>> GetTransfersAsync(
        string chainTag, string tokenContractAddress, long fromBlock, long toBlock, CancellationToken ct);
}
