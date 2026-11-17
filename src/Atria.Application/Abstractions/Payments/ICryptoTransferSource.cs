using Atria.Domain.Payments;

namespace Atria.Application.Abstractions;

/// <summary>A confirmed token transfer into a receiving wallet, as read off the chain.</summary>
/// <param name="TransferKey">Unique per transfer (transaction + log position where a chain has several).</param>
/// <param name="Amount">Token amount in whole units (decimals already applied).</param>
public sealed record IncomingTransfer(
    string TransferKey,
    string TransactionHash,
    string FromAddress,
    string ToAddress,
    decimal Amount,
    DateTime OccurredAtUtc);

/// <summary>One page read by <see cref="ICryptoTransferSource.ReadAsync"/> and where to continue from.</summary>
public sealed record TransferPage(IReadOnlyList<IncomingTransfer> Transfers, long NextPosition);

/// <summary>Reads confirmed incoming transfers of one token into one wallet from a chain.</summary>
/// <remarks>
/// A position is opaque to callers — a block number on BNB Smart Chain, a millisecond timestamp on
/// TRON. Reading the same position twice may return the same transfers again; callers dedupe by
/// <see cref="IncomingTransfer.TransferKey"/>.
/// </remarks>
public interface ICryptoTransferSource
{
    bool Supports(CryptoPaymentMethod method);

    /// <summary>Where to start reading a wallet seen for the first time: "now", not the chain's history.</summary>
    Task<long> GetStartPositionAsync(CryptoPaymentMethod method, CancellationToken ct);

    Task<TransferPage> ReadAsync(
        CryptoPaymentMethod method, string tokenContract, string toAddress, long fromPosition,
        CancellationToken ct);
}
