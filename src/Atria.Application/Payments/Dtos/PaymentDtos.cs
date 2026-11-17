using Atria.Domain.Payments;

namespace Atria.Application.Payments.Dtos;

public sealed record PaymentSettingsDto(
    decimal? UsdtRateKgs,
    string? Bep20Address,
    string? Trc20Address,
    string? KgstContractAddress,
    IReadOnlyList<string> EnabledMethods,
    DateTime? UpdatedAtUtc)
{
    public static PaymentSettingsDto From(PaymentSettings? s) => s is null
        ? new PaymentSettingsDto(null, null, null, null, Array.Empty<string>(), null)
        : new PaymentSettingsDto(
            s.UsdtRateKgs, s.Bep20Address, s.Trc20Address, s.KgstContractAddress,
            s.EnabledMethods().Select(m => m.ToWire()).ToList(), s.UpdatedAtUtc ?? s.CreatedAtUtc);
}

/// <summary>A method an investor can pick, with the rate the price will be converted at.</summary>
public sealed record PaymentMethodDto(string Method, string Token, string Network, decimal RateKgs);

public sealed record CryptoInvoiceDto(
    Guid Id,
    Guid InvestmentId,
    string Method,
    string Token,
    string Network,
    string PayToAddress,
    decimal PayAmount,
    decimal RateKgs,
    decimal AmountKgs,
    DateTime ExpiresAtUtc,
    string Status,
    string? TransactionHash,
    string? TransactionUrl,
    string? FromAddress,
    DateTime? PaidAtUtc,
    DateTime CreatedAtUtc)
{
    public static CryptoInvoiceDto From(CryptoInvoice i) => new(
        i.Id, i.InvestmentId, i.Method.ToWire(), i.Method.Token(), i.Method.Network(), i.PayToAddress,
        i.PayAmount, i.RateKgs, i.AmountKgs, i.ExpiresAtUtc, StatusWire(i.Status), i.TransactionHash,
        CryptoExplorer.TransactionUrl(i.Method, i.TransactionHash), i.FromAddress, i.PaidAtUtc, i.CreatedAtUtc);

    public static string StatusWire(CryptoInvoiceStatus status) => status switch
    {
        CryptoInvoiceStatus.Pending => "pending",
        CryptoInvoiceStatus.Paid => "paid",
        CryptoInvoiceStatus.Cancelled => "cancelled",
        _ => status.ToString().ToLowerInvariant()
    };
}

public sealed record CryptoDepositDto(
    Guid Id,
    string Method,
    string Token,
    string Network,
    string TransactionHash,
    string? TransactionUrl,
    string FromAddress,
    string ToAddress,
    decimal Amount,
    DateTime OccurredAtUtc,
    Guid? InvoiceId)
{
    public static CryptoDepositDto From(CryptoDeposit d) => new(
        d.Id, d.Method.ToWire(), d.Method.Token(), d.Method.Network(), d.TransactionHash,
        CryptoExplorer.TransactionUrl(d.Method, d.TransactionHash), d.FromAddress, d.ToAddress, d.Amount,
        d.OccurredAtUtc, d.InvoiceId);
}

public static class CryptoExplorer
{
    public static string? TransactionUrl(CryptoPaymentMethod method, string? txHash)
    {
        if (string.IsNullOrWhiteSpace(txHash))
            return null;

        return method == CryptoPaymentMethod.UsdtTrc20
            ? $"https://tronscan.org/#/transaction/{txHash}"
            : $"https://bscscan.com/tx/{txHash}";
    }
}
