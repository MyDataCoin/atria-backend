using System.Text.RegularExpressions;
using Atria.Domain.Common;

namespace Atria.Domain.Payments;

/// <summary>
/// Operator-set crypto payment configuration: the receiving wallets and the USDT rate. One row.
/// </summary>
/// <remarks>
/// The rate is set by the operator, not pulled from a feed: it is the rate the platform actually
/// converts at, and it is fixed into every invoice when the invoice is issued. A method is offered
/// to investors only when it has a wallet (and, for USDT, a rate) — so leaving a field empty is how
/// a method is switched off.
/// </remarks>
public sealed partial class PaymentSettings : AggregateRoot
{
    /// <summary>The single row's id.</summary>
    public static readonly Guid SingletonId = new("7a1c0b6e-5f0d-4c55-9e0e-6b9c3c0a0001");

    public const int MaxAddressLength = 64;

    /// <summary>How many som one USDT is converted at. Null switches both USDT methods off.</summary>
    public decimal? UsdtRateKgs { get; private set; }

    /// <summary>Receiving wallet for BEP-20 payments (USDT and KGST on BNB Smart Chain).</summary>
    public string? Bep20Address { get; private set; }

    /// <summary>Receiving wallet for USDT on TRON.</summary>
    public string? Trc20Address { get; private set; }

    /// <summary>KGST token contract on BNB Smart Chain. Null switches KGST off.</summary>
    public string? KgstContractAddress { get; private set; }

    /// <summary>Who changed the settings last.</summary>
    public Guid? UpdatedBy { get; private set; }

    private PaymentSettings() { }

    public static PaymentSettings CreateEmpty() => new() { Id = SingletonId };

    public void Update(
        decimal? usdtRateKgs, string? bep20Address, string? trc20Address, string? kgstContractAddress,
        Guid updatedBy)
    {
        if (usdtRateKgs is { } rate && (rate <= 0 || rate > 100_000))
            throw new DomainException("The USDT rate must be a positive number of som.");

        var bep20 = Normalize(bep20Address);
        var trc20 = Normalize(trc20Address);
        var kgst = Normalize(kgstContractAddress);

        if (bep20 is not null && !EvmAddress().IsMatch(bep20))
            throw new DomainException("The BEP-20 wallet must be a 0x… address of 40 hex characters.");
        if (kgst is not null && !EvmAddress().IsMatch(kgst))
            throw new DomainException("The KGST contract must be a 0x… address of 40 hex characters.");
        if (trc20 is not null && !TronAddress().IsMatch(trc20))
            throw new DomainException("The TRC-20 wallet must be a TRON address starting with T (34 characters).");

        UsdtRateKgs = usdtRateKgs is { } r ? decimal.Round(r, 4) : null;
        Bep20Address = bep20;
        Trc20Address = trc20;
        KgstContractAddress = kgst;
        UpdatedBy = updatedBy;
    }

    /// <summary>The wallet a method pays into, or null when the method is not configured.</summary>
    public string? AddressFor(CryptoPaymentMethod method) => method switch
    {
        CryptoPaymentMethod.UsdtBep20 or CryptoPaymentMethod.KgstBep20 => Bep20Address,
        CryptoPaymentMethod.UsdtTrc20 => Trc20Address,
        _ => null
    };

    /// <summary>Som per one unit of the method's token, or null when no rate is set.</summary>
    public decimal? RateFor(CryptoPaymentMethod method)
        => method.IsSomPegged() ? 1m : UsdtRateKgs;

    /// <summary>Can investors be offered this method right now?</summary>
    public bool IsEnabled(CryptoPaymentMethod method)
        => AddressFor(method) is not null
           && RateFor(method) is not null
           && (method != CryptoPaymentMethod.KgstBep20 || KgstContractAddress is not null);

    public IReadOnlyList<CryptoPaymentMethod> EnabledMethods()
        => Enum.GetValues<CryptoPaymentMethod>().Where(IsEnabled).ToList();

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^0x[0-9a-fA-F]{40}$")]
    private static partial Regex EvmAddress();

    [GeneratedRegex("^T[1-9A-HJ-NP-Za-km-z]{33}$")]
    private static partial Regex TronAddress();
}
