using Atria.Domain.Common;

namespace Atria.Domain.Payments;

/// <summary>
/// One incoming token transfer seen on a receiving wallet — matched to an invoice or not.
/// </summary>
/// <remarks>
/// Every transfer is kept, not only the ones that matched: a wrong amount, a late payment or a
/// transfer from nowhere is still somebody's money on the platform's wallet, and the operator has
/// to see it to return or reconcile it. <see cref="TransferKey"/> (transaction + log position) makes
/// re-reading the same block range harmless.
/// </remarks>
public sealed class CryptoDeposit : AggregateRoot
{
    public const int MaxKeyLength = 140;

    public CryptoPaymentMethod Method { get; private set; }

    /// <summary>Unique per transfer: "{txHash}:{logIndex}" on BNB, the transaction id on TRON.</summary>
    public string TransferKey { get; private set; } = null!;

    public string TransactionHash { get; private set; } = null!;
    public string FromAddress { get; private set; } = null!;
    public string ToAddress { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    /// <summary>The invoice this transfer paid, or null when it matched none.</summary>
    public Guid? InvoiceId { get; private set; }

    private CryptoDeposit() { }

    public static CryptoDeposit Record(
        CryptoPaymentMethod method, string transferKey, string transactionHash, string fromAddress,
        string toAddress, decimal amount, DateTime occurredAtUtc)
        => new()
        {
            Id = Guid.NewGuid(),
            Method = method,
            TransferKey = transferKey,
            TransactionHash = transactionHash,
            FromAddress = fromAddress,
            ToAddress = toAddress,
            Amount = amount,
            OccurredAtUtc = occurredAtUtc
        };

    public void MatchTo(Guid invoiceId) => InvoiceId = invoiceId;
}
