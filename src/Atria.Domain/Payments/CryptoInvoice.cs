using Atria.Domain.Common;

namespace Atria.Domain.Payments;

public enum CryptoInvoiceStatus
{
    /// <summary>Issued, waiting for the transfer.</summary>
    Pending = 0,

    /// <summary>A transfer of exactly <see cref="CryptoInvoice.PayAmount"/> arrived on the wallet.</summary>
    Paid = 1,

    /// <summary>Replaced by another invoice (the investor switched method) or the application ended.</summary>
    Cancelled = 2
}

/// <summary>
/// What an investor must send, in which token and to which wallet, to pay one application.
/// </summary>
/// <remarks>
/// <para>
/// The amount carries a per-invoice "fingerprint" in its last two decimals (e.g. 25 000.37 USDT). A
/// transfer on a public chain says nothing about who sent it; the exact amount is what ties it to this
/// application. Amounts are therefore unique among the pending invoices of one method.
/// </para>
/// <para>
/// The rate is copied in at issue time and never changes afterwards: the investor is quoted a figure
/// and pays that figure, whatever the operator sets the rate to later.
/// </para>
/// </remarks>
public sealed class CryptoInvoice : AggregateRoot
{
    public Guid InvestmentId { get; private set; }
    public Guid InvestorId { get; private set; }
    public CryptoPaymentMethod Method { get; private set; }
    public string PayToAddress { get; private set; } = null!;

    /// <summary>Exact token amount to send, two decimals, fingerprint included.</summary>
    public decimal PayAmount { get; private set; }

    /// <summary>Som per token at issue time (1 for KGST).</summary>
    public decimal RateKgs { get; private set; }

    /// <summary>The application's price in som — what <see cref="PayAmount"/> was computed from.</summary>
    public decimal AmountKgs { get; private set; }

    /// <summary>After this the invoice is not matched (the application's reservation ends then).</summary>
    public DateTime ExpiresAtUtc { get; private set; }

    public CryptoInvoiceStatus Status { get; private set; }

    public string? TransactionHash { get; private set; }
    public string? FromAddress { get; private set; }
    public DateTime? PaidAtUtc { get; private set; }

    private CryptoInvoice() { }

    /// <summary>
    /// Issues an invoice. <paramref name="fingerprintCents"/> (1–99) is added to the converted amount
    /// so that the transfer can be told apart from every other pending one.
    /// </summary>
    public static CryptoInvoice Issue(
        Guid investmentId, Guid investorId, CryptoPaymentMethod method, string payToAddress,
        decimal amountKgs, decimal rateKgs, int fingerprintCents, DateTime expiresAtUtc)
    {
        if (amountKgs <= 0)
            throw new DomainException("The application amount must be positive.");
        if (rateKgs <= 0)
            throw new DomainException("The rate must be positive.");
        if (fingerprintCents is < 1 or > 9_999)
            throw new DomainException("The amount fingerprint is out of range.");
        if (string.IsNullOrWhiteSpace(payToAddress))
            throw new DomainException("A receiving wallet is required.");

        return new CryptoInvoice
        {
            Id = Guid.NewGuid(),
            InvestmentId = investmentId,
            InvestorId = investorId,
            Method = method,
            PayToAddress = payToAddress,
            PayAmount = BaseAmount(amountKgs, rateKgs) + fingerprintCents / 100m,
            RateKgs = rateKgs,
            AmountKgs = amountKgs,
            ExpiresAtUtc = expiresAtUtc,
            Status = CryptoInvoiceStatus.Pending
        };
    }

    /// <summary>The som amount converted to the token, rounded UP to the cent — never under-charged.</summary>
    public static decimal BaseAmount(decimal amountKgs, decimal rateKgs)
        => Math.Ceiling(amountKgs / rateKgs * 100m) / 100m;

    public bool IsOpenAt(DateTime nowUtc) => Status == CryptoInvoiceStatus.Pending && nowUtc <= ExpiresAtUtc;

    public void MarkPaid(string transactionHash, string fromAddress, DateTime paidAtUtc)
    {
        if (Status != CryptoInvoiceStatus.Pending)
            throw new DomainException("Only a pending invoice can be paid.");
        if (string.IsNullOrWhiteSpace(transactionHash))
            throw new DomainException("A transaction hash is required.");

        Status = CryptoInvoiceStatus.Paid;
        TransactionHash = transactionHash;
        FromAddress = fromAddress;
        PaidAtUtc = paidAtUtc;
    }

    public void Cancel()
    {
        if (Status == CryptoInvoiceStatus.Pending)
            Status = CryptoInvoiceStatus.Cancelled;
    }
}
